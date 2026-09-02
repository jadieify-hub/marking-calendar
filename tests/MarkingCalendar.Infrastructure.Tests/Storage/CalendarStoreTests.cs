using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class CalendarStoreTests
{
    [Fact]
    public async Task SaveValidatedAsync_PersistsSnapshotThatCanBeReloaded()
    {
        using var temp = new TemporaryDirectory();
        using var store = CreateStore(temp.Path);
        var snapshot = Snapshot([Event(1)]);

        var result = await store.SaveValidatedAsync(snapshot, CancellationToken.None);
        var loaded = await store.LoadCurrentAsync(CancellationToken.None);

        Assert.True(result.Saved);
        Assert.Equal(snapshot, loaded);
    }

    [Fact]
    public async Task SaveValidatedAsync_DoesNotReplaceCurrentWithInvalidSnapshot()
    {
        using var temp = new TemporaryDirectory();
        using var store = CreateStore(temp.Path);
        var baseline = Snapshot([Event(1)]);
        await store.SaveValidatedAsync(baseline, CancellationToken.None);

        var result = await store.SaveValidatedAsync(Snapshot([]), CancellationToken.None);
        var loaded = await store.LoadCurrentAsync(CancellationToken.None);

        Assert.False(result.Saved);
        Assert.Contains(result.Validation.Errors, error => error.Code == SnapshotValidationErrorCode.Empty);
        Assert.Equal(baseline, loaded);
    }

    [Fact]
    public async Task LoadCurrentAsync_IgnoresStaleTemporaryFile()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        using var store = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter());
        var snapshot = Snapshot([Event(1)]);
        await store.SaveValidatedAsync(snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(paths.CurrentSnapshot + ".stale.tmp", "broken", CancellationToken.None);

        var loaded = await store.LoadCurrentAsync(CancellationToken.None);

        Assert.Equal(snapshot, loaded);
    }

    [Fact]
    public async Task LoadCurrentAsync_QuarantinesTruncatedSnapshotAndReturnsNull()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.CurrentSnapshot, "{\"id\":\"truncated");
        var logger = new RecordingLogger();
        using var store = new CalendarStore(
            paths,
            new SnapshotValidator(),
            new AtomicFileWriter(),
            timeProvider: new FixedTimeProvider(),
            logger: logger);

        var loaded = await store.LoadCurrentAsync(CancellationToken.None);

        Assert.Null(loaded);
        Assert.False(File.Exists(paths.CurrentSnapshot));
        Assert.Single(Directory.GetFiles(paths.DataDirectory, "current.corrupt-20260902-070506*.json"));
        Assert.Contains(logger.Entries, entry => entry.Level == AppLogLevel.Warning && entry.Source == "storage");
    }

    [Fact]
    public async Task LoadHistoryAsync_QuarantinesTruncatedHistoryAndReturnsEmpty()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.ChangeHistoryFile, "{\"batches\":[");
        using var store = new CalendarStore(
            paths,
            new SnapshotValidator(),
            new AtomicFileWriter(),
            timeProvider: new FixedTimeProvider());

        var loaded = await store.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(ChangeHistory.Empty, loaded);
        Assert.False(File.Exists(paths.ChangeHistoryFile));
        Assert.Single(Directory.GetFiles(paths.HistoryDirectory, "changes.corrupt-20260902-070506*.json"));
    }

    [Fact]
    public async Task LoadHistoryAsync_DefaultsLineageForLegacyBatch()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.ChangeHistoryFile, """
            {
              "batches": [{
                "id": "legacy",
                "checkedAt": "2026-09-02T07:00:00Z",
                "changes": { "added": [], "removed": [], "moved": [], "changed": [] }
              }]
            }
            """);
        using var store = CreateStore(temp.Path);

        var batch = Assert.Single((await store.LoadHistoryAsync(CancellationToken.None)).Batches);

        Assert.Null(batch.PreviousSnapshotId);
        Assert.Null(batch.CurrentSnapshotId);
        Assert.Equal("local", batch.Source);
        Assert.Empty(batch.Changes.GroupsAdded);
        Assert.Empty(batch.Changes.GroupsRemoved);
        Assert.Empty(batch.Changes.GroupsRenamed);
    }

    [Fact]
    public async Task AppendHistoryAsync_UsesConfiguredBatchLimit()
    {
        using var temp = new TemporaryDirectory();
        using var store = new CalendarStore(
            new AppPaths(temp.Path),
            new SnapshotValidator(),
            new AtomicFileWriter(),
            maxHistoryBatches: 2);
        for (var index = 0; index < 3; index++)
        {
            await store.AppendHistoryAsync(
                new ChangeBatch($"batch-{index}", new DateTimeOffset(2026, 9, index + 1, 7, 0, 0, TimeSpan.Zero), ChangeSet.Empty),
                CancellationToken.None);
        }

        var history = await store.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(["batch-2", "batch-1"], history.Batches.Select(item => item.Id));
    }

    [Fact]
    public async Task SaveHistoryAsync_ReplacesHistoryWithinConfiguredLimit()
    {
        using var temp = new TemporaryDirectory();
        using var store = new CalendarStore(
            new AppPaths(temp.Path),
            new SnapshotValidator(),
            new AtomicFileWriter(),
            maxHistoryBatches: 2);
        var history = new ChangeHistory(Enumerable.Range(1, 3)
            .Select(index => new ChangeBatch($"batch-{index}", new DateTimeOffset(2026, 9, index, 7, 0, 0, TimeSpan.Zero), ChangeSet.Empty))
            .ToArray());

        await store.SaveHistoryAsync(history, CancellationToken.None);

        Assert.Equal(["batch-3", "batch-2"], (await store.LoadHistoryAsync(CancellationToken.None)).Batches.Select(batch => batch.Id));
    }

    [Fact]
    public async Task LoadLatestArchiveAsync_SkipsCorruptNewestArchiveAndReturnsValidSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        using var store = new CalendarStore(
            paths,
            new SnapshotValidator(),
            new AtomicFileWriter(),
            timeProvider: new FixedTimeProvider());
        var archived = Snapshot([Event(1)]);
        await store.SaveValidatedAsync(archived, CancellationToken.None);
        await store.SaveValidatedAsync(Snapshot([Event(2)]), CancellationToken.None);
        var corruptArchive = Path.Combine(paths.ArchiveDirectory, "99999999-999999-corrupt.json");
        await File.WriteAllTextAsync(corruptArchive, "{");
        File.SetLastWriteTimeUtc(corruptArchive, new DateTime(2030, 1, 1));

        var loaded = await store.LoadLatestArchiveAsync(CancellationToken.None);

        Assert.Equal(archived, loaded);
        Assert.False(File.Exists(corruptArchive));
        Assert.Contains(Directory.GetFiles(paths.ArchiveDirectory), path => Path.GetFileName(path).Contains("corrupt-20260902-070506", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListArchivesAsync_SortsBySnapshotStampAndIgnoresUnrecognizedFiles()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(Path.Combine(paths.ArchiveDirectory, "20260801-090000-older.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(paths.ArchiveDirectory, "20260902-070000-newer.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(paths.ArchiveDirectory, "notes.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(paths.ArchiveDirectory, "20261001-000000-bad.corrupt-20261002.json"), "{}");
        using var store = CreateStore(temp.Path);

        var archives = await store.ListArchivesAsync(CancellationToken.None);

        Assert.Equal(["20260902-070000-newer.json", "20260801-090000-older.json"], archives.Select(item => item.Id));
        Assert.Equal(
            [new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)],
            archives.Select(item => item.RetrievedAt));
    }

    [Fact]
    public void Enforce_RemovesOldArchivesAndLogsWithinConfiguredLimits()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        for (var index = 0; index < 25; index++)
        {
            File.WriteAllText(Path.Combine(paths.ArchiveDirectory, $"archive-{index:00}.json"), "{}");
        }

        for (var index = 0; index < 35; index++)
        {
            File.WriteAllText(Path.Combine(paths.LogDirectory, $"log-{index:00}.log"), "line");
        }

        new RetentionPolicy(maxArchives: 20, maxLogs: 30).Enforce(paths);

        Assert.Equal(20, Directory.GetFiles(paths.ArchiveDirectory).Length);
        Assert.Equal(30, Directory.GetFiles(paths.LogDirectory).Length);
    }

    private static CalendarStore CreateStore(string root) =>
        new(new AppPaths(root), new SnapshotValidator(), new AtomicFileWriter());

    private static CalendarSnapshot Snapshot(IReadOnlyList<CalendarEvent> events) =>
        CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), new Uri("https://example.test/source"), events);

    private static CalendarEvent Event(int index)
    {
        var date = new DateOnly(2026, 9, index);
        return new CalendarEvent($"event-{index}", date, null, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), $"Группа {index}", "Маркировка", "Старт", "Описание", null);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 5, 6, TimeSpan.Zero);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(AppLogLevel Level, string Source)> Entries { get; } = [];

        public void Log(AppLogLevel level, string source, string message, Exception? exception = null) =>
            Entries.Add((level, source));

        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
