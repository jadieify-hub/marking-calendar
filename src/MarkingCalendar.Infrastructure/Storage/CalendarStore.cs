using System.Globalization;
using System.Text.Json;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed record SnapshotArchiveInfo(string Id, DateTimeOffset RetrievedAt);

public sealed class CalendarStore(
    AppPaths paths,
    ISnapshotValidator validator,
    IAtomicFileWriter writer,
    RetentionPolicy? retentionPolicy = null,
    TimeProvider? timeProvider = null,
    IAppLogger? logger = null,
    int maxHistoryBatches = 50)
    : IDisposable
{
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ISnapshotValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly RetentionPolicy _retentionPolicy = retentionPolicy ?? new RetentionPolicy();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IAppLogger? _logger = logger;
    private readonly int _maxHistoryBatches = maxHistoryBatches > 0
        ? maxHistoryBatches
        : throw new ArgumentOutOfRangeException(nameof(maxHistoryBatches), "Лимит истории должен быть больше нуля.");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CalendarSnapshot?> LoadCurrentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.CurrentSnapshot))
        {
            return null;
        }

        try
        {
            var snapshot = await ReadJsonAsync<CalendarSnapshot>(_paths.CurrentSnapshot, cancellationToken).ConfigureAwait(false);
            EnsureValidSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception error) when (IsCorruptData(error))
        {
            Quarantine(_paths.CurrentSnapshot, "Текущий снимок календаря повреждён.", error);
            return null;
        }
    }

    public async Task<CalendarSnapshot?> LoadLatestArchiveAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        var archives = new DirectoryInfo(_paths.ArchiveDirectory)
            .EnumerateFiles("*.json")
            .Where(file => !file.Name.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var archive in archives)
        {
            try
            {
                var snapshot = await ReadJsonAsync<CalendarSnapshot>(archive.FullName, cancellationToken).ConfigureAwait(false);
                EnsureValidSnapshot(snapshot);
                return snapshot;
            }
            catch (Exception error) when (IsCorruptData(error))
            {
                Quarantine(archive.FullName, $"Резервная копия {archive.Name} повреждена.", error);
            }
        }

        return null;
    }

    public Task<IReadOnlyList<SnapshotArchiveInfo>> ListArchivesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureCreated();
        IReadOnlyList<SnapshotArchiveInfo> result = new DirectoryInfo(_paths.ArchiveDirectory)
            .EnumerateFiles("*.json")
            .Select(file => ArchiveInfo(file.Name))
            .OfType<SnapshotArchiveInfo>()
            .OrderByDescending(item => item.RetrievedAt)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .Take(20)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<CalendarSnapshot?> LoadArchiveAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var archives = await ListArchivesAsync(cancellationToken).ConfigureAwait(false);
        var archive = archives.FirstOrDefault(item => item.Id.Equals(id, StringComparison.Ordinal));
        if (archive is null) return null;
        var path = Path.Combine(_paths.ArchiveDirectory, archive.Id);
        try
        {
            var snapshot = await ReadJsonAsync<CalendarSnapshot>(path, cancellationToken).ConfigureAwait(false);
            EnsureValidSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception error) when (IsCorruptData(error))
        {
            Quarantine(path, $"Резервная копия {archive.Id} повреждена.", error);
            return null;
        }
    }

    public async Task<SnapshotSaveResult> SaveValidatedAsync(
        CalendarSnapshot candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var baseline = await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
            var validation = _validator.Validate(candidate, baseline);
            if (!validation.IsValid)
            {
                return SnapshotSaveResult.Rejected(validation);
            }

            if (baseline is not null && baseline.Id != candidate.Id)
            {
                var stamp = baseline.RetrievedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var archive = Path.Combine(_paths.ArchiveDirectory, $"{stamp}-{baseline.Id[..Math.Min(12, baseline.Id.Length)]}.json");
                await _writer.WriteJsonAsync(archive, baseline, cancellationToken).ConfigureAwait(false);
            }

            await _writer.WriteJsonAsync(_paths.CurrentSnapshot, candidate, cancellationToken).ConfigureAwait(false);
            _retentionPolicy.Enforce(_paths);
            return SnapshotSaveResult.Success;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeHistory> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.ChangeHistoryFile))
        {
            return ChangeHistory.Empty;
        }

        try
        {
            var history = await ReadJsonAsync<ChangeHistory>(_paths.ChangeHistoryFile, cancellationToken).ConfigureAwait(false);
            if (history?.Batches is null)
            {
                throw new InvalidDataException("История изменений пуста или повреждена.");
            }

            return history;
        }
        catch (Exception error) when (IsCorruptData(error))
        {
            Quarantine(_paths.ChangeHistoryFile, "История изменений повреждена.", error);
            return ChangeHistory.Empty;
        }
    }

    public async Task AppendHistoryAsync(ChangeBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var history = await LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
            if (history.Batches.Any(existing => existing.Id == batch.Id))
            {
                return;
            }

            var batches = history.Batches
                .Append(batch)
                .OrderByDescending(item => item.CheckedAt)
                .Take(_maxHistoryBatches)
                .ToArray();
            await _writer.WriteJsonAsync(_paths.ChangeHistoryFile, new ChangeHistory(batches), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveHistoryAsync(ChangeHistory history, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = history.Batches
                .Where(batch => batch is not null)
                .GroupBy(batch => batch.Id, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(batch => batch.CheckedAt).First())
                .OrderByDescending(batch => batch.CheckedAt)
                .Take(_maxHistoryBatches)
                .ToArray();
            await _writer.WriteJsonAsync(_paths.ChangeHistoryFile, new ChangeHistory(normalized), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureValidSnapshot(CalendarSnapshot? snapshot)
    {
        if (snapshot is null
            || string.IsNullOrWhiteSpace(snapshot.Id)
            || snapshot.SourceUrl is null
            || snapshot.Events is null
            || snapshot.Events.Any(item => item is null))
        {
            throw new InvalidDataException("Снимок календаря не содержит обязательные данные.");
        }

        var validation = _validator.Validate(snapshot, baseline: null);
        var calculatedId = CalendarSnapshot.Create(snapshot.RetrievedAt, snapshot.SourceUrl, snapshot.Events).Id;
        if (!validation.IsValid || !snapshot.Id.Equals(calculatedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Снимок календаря не прошёл проверку целостности.");
        }
    }

    private void Quarantine(string path, string message, Exception error)
    {
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var directory = Path.GetDirectoryName(path) ?? _paths.RootDirectory;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var target = Path.Combine(directory, $"{stem}.corrupt-{stamp}{extension}");
        for (var suffix = 2; File.Exists(target); suffix++)
        {
            target = Path.Combine(directory, $"{stem}.corrupt-{stamp}-{suffix.ToString(CultureInfo.InvariantCulture)}{extension}");
        }

        try
        {
            File.Move(path, target);
            _logger?.Log(AppLogLevel.Warning, "storage", $"{message} Файл перемещён в {Path.GetFileName(target)}.", error);
        }
        catch (Exception quarantineError)
        {
            _logger?.Log(AppLogLevel.Error, "storage", $"{message} Не удалось переместить повреждённый файл.", quarantineError);
        }
    }

    private static bool IsCorruptData(Exception error) =>
        error is JsonException or NotSupportedException or InvalidDataException;

    private static SnapshotArchiveInfo? ArchiveInfo(string fileName)
    {
        if (fileName.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || fileName.Length <= 22
            || fileName[8] != '-'
            || fileName[15] != '-')
        {
            return null;
        }

        return DateTime.TryParseExact(
            fileName[..15],
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var stamp)
                ? new SnapshotArchiveInfo(fileName, new DateTimeOffset(stamp, TimeSpan.Zero))
                : null;
    }
}
