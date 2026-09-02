using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Migration;

public sealed class LegacyCalendarImporter(
    string legacyDirectory,
    AppPaths currentPaths,
    CalendarStore store,
    IEventNormalizer normalizer,
    IAppLogger? logger = null)
{
    private const string Assignment = "window.CHZ_CALENDAR_DATA";
    private readonly string _legacyDirectory = Path.GetFullPath(legacyDirectory ?? throw new ArgumentNullException(nameof(legacyDirectory)));
    private readonly AppPaths _currentPaths = currentPaths ?? throw new ArgumentNullException(nameof(currentPaths));
    private readonly CalendarStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IEventNormalizer _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    private readonly IAppLogger? _logger = logger;

    public async Task<LegacyImportResult> ImportOnceAsync(CancellationToken cancellationToken)
    {
        _currentPaths.EnsureCreated();
        if (File.Exists(_currentPaths.MigrationMarker))
        {
            return new LegacyImportResult(LegacyImportStatus.AlreadyImported, "Данные прежней версии уже обработаны.");
        }

        var calendarFile = Path.Combine(_legacyDirectory, "calendar-data.js");
        if (!File.Exists(calendarFile))
        {
            return new LegacyImportResult(LegacyImportStatus.NotFound, "Данные прежней версии не найдены.");
        }

        if (await _store.LoadCurrentAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            await WriteMarkerAsync("existing-data", cancellationToken).ConfigureAwait(false);
            return new LegacyImportResult(LegacyImportStatus.ExistingData, "Текущее хранилище уже содержит календарь.");
        }

        string? script = null;
        try
        {
            script = await File.ReadAllTextAsync(calendarFile, cancellationToken).ConfigureAwait(false);
            var payload = ParseAssignedPayload(script);
            var events = payload.Events.Select(item => _normalizer.Normalize(new SourceEvent(
                item.Start,
                item.End,
                item.Period,
                item.Group,
                item.Type,
                item.Stage,
                item.Description,
                item.Url))).ToArray();
            var retrievedAt = DateTimeOffset.TryParse(payload.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
            var sourceUrl = Uri.TryCreate(payload.SourceUrl, UriKind.Absolute, out var source)
                ? source
                : MarkingCalendarClient.Endpoint;
            var snapshot = CalendarSnapshot.Create(retrievedAt, sourceUrl, events);
            var saved = await _store.SaveValidatedAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (!saved.Saved)
            {
                var message = string.Join(' ', saved.Validation.Errors.Select(error => error.Message));
                _logger?.Log(AppLogLevel.Warning, "legacy-import", $"Данные прежней версии отклонены: {message}");
                await SaveRejectedAsync(script, cancellationToken).ConfigureAwait(false);
                return new LegacyImportResult(
                    LegacyImportStatus.Rejected,
                    message);
            }

            var importedHistory = await ImportHistoryAsync(cancellationToken).ConfigureAwait(false);
            await WriteMarkerAsync(snapshot.Id, cancellationToken).ConfigureAwait(false);
            var importMessage = $"Импортировано событий: {events.Length}; пакетов истории: {importedHistory}.";
            _logger?.Log(AppLogLevel.Info, "legacy-import", importMessage);
            return new LegacyImportResult(LegacyImportStatus.Imported, importMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException or CalendarEventValidationException or InvalidDataException)
        {
            _logger?.Log(AppLogLevel.Warning, "legacy-import", "Данные прежней версии не прошли проверку.", error);
            await SaveRejectedAsync(script, CancellationToken.None).ConfigureAwait(false);
            return new LegacyImportResult(LegacyImportStatus.Rejected, "Данные прежней версии не прошли проверку.", error);
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "legacy-import", "Не удалось импортировать данные прежней версии.", error);
            return new LegacyImportResult(LegacyImportStatus.Failed, "Не удалось импортировать данные прежней версии.", error);
        }
    }

    private Task SaveRejectedAsync(string? script, CancellationToken cancellationToken) =>
        _logger is null || script is null
            ? Task.CompletedTask
            : _logger.SaveRejectedJsonAsync(
                "legacy-import",
                JsonSerializer.Serialize(new { legacyScript = script }, JsonDefaults.Options),
                cancellationToken);

    private async Task<int> ImportHistoryAsync(CancellationToken cancellationToken)
    {
        var historyPath = Path.Combine(_legacyDirectory, "change-history.json");
        if (!File.Exists(historyPath)) return 0;

        string json;
        try
        {
            json = await File.ReadAllTextAsync(historyPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger?.Log(AppLogLevel.Warning, "legacy-import", "Не удалось прочитать историю прежней версии.", error);
            return 0;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            _logger?.Log(AppLogLevel.Warning, "legacy-import", "История прежней версии не прошла проверку.", error);
            if (_logger is not null)
            {
                await _logger.SaveRejectedJsonAsync("legacy-history", json, CancellationToken.None).ConfigureAwait(false);
            }

            return 0;
        }

        using (document)
        {
            var root = document.RootElement;
            var parsedBatches = new List<ChangeBatch>();
            if (root.TryGetProperty("batches", out var batches) && batches.ValueKind == JsonValueKind.Array)
            {
                foreach (var batchNode in batches.EnumerateArray())
                {
                    var batch = await ParseHistoryBatchAsync(batchNode).ConfigureAwait(false);
                    if (batch is not null) parsedBatches.Add(batch);
                }
            }
            else
            {
                var batch = await ParseHistoryBatchAsync(root).ConfigureAwait(false);
                if (batch is not null) parsedBatches.Add(batch);
            }

            foreach (var batch in parsedBatches)
            {
                await _store.AppendHistoryAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            return parsedBatches.Count;
        }
    }

    private async Task<ChangeBatch?> ParseHistoryBatchAsync(JsonElement batchNode)
    {
        try
        {
            var checkedAtText = RequiredText(batchNode, "checkedAt");
            if (!DateTimeOffset.TryParse(
                    checkedAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var checkedAt))
            {
                throw new InvalidDataException("Пакет истории содержит некорректную дату проверки.");
            }

            var changes = new ChangeSet(
                ReadEvents(batchNode, "added"),
                ReadEvents(batchNode, "removed"),
                ReadChanges(batchNode, "moved", ChangeKind.Moved),
                ReadChanges(batchNode, "changed", ChangeKind.Changed));
            if (!changes.HasChanges) return null;
            return new ChangeBatch(ChangeBatchIdFactory.FromChanges(checkedAt, changes), checkedAt, changes);
        }
        catch (Exception error) when (error is JsonException
            or InvalidDataException
            or CalendarEventValidationException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            _logger?.Log(AppLogLevel.Warning, "legacy-import", "Пакет истории прежней версии пропущен.", error);
            if (_logger is not null)
            {
                await _logger.SaveRejectedJsonAsync(
                    "legacy-history-batch",
                    batchNode.GetRawText(),
                    CancellationToken.None).ConfigureAwait(false);
            }

            return null;
        }
    }

    private CalendarEvent[] ReadEvents(JsonElement batchNode, string property)
    {
        if (!batchNode.TryGetProperty(property, out var eventsNode) || eventsNode.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (eventsNode.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Поле {property} пакета истории должно быть массивом.");
        }

        return eventsNode.EnumerateArray().Select(ReadEvent).ToArray();
    }

    private EventChange[] ReadChanges(JsonElement batchNode, string property, ChangeKind kind)
    {
        if (!batchNode.TryGetProperty(property, out var changesNode) || changesNode.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (changesNode.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Поле {property} пакета истории должно быть массивом.");
        }

        return changesNode.EnumerateArray().Select(item =>
        {
            var previous = ReadEvent(item.GetProperty("previous"));
            var current = ReadEvent(item.GetProperty("event"));
            return kind == ChangeKind.Moved
                ? EventChange.Moved(previous, current)
                : EventChange.Changed(previous, current);
        }).ToArray();
    }

    private CalendarEvent ReadEvent(JsonElement node)
    {
        var item = node.Deserialize<LegacyEvent>(JsonDefaults.Options)
            ?? throw new InvalidDataException("Событие истории пусто.");
        return _normalizer.Normalize(new SourceEvent(
            item.Start,
            item.End,
            item.Period,
            item.Group,
            item.Type,
            item.Stage,
            item.Description,
            item.Url));
    }

    private static string RequiredText(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Пакет истории не содержит {property}.");

    private static LegacyPayload ParseAssignedPayload(string script)
    {
        var trimmed = script.Trim();
        if (!trimmed.StartsWith(Assignment, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Файл не содержит ожидаемое присваивание календаря.");
        }

        var equals = trimmed.IndexOf('=', Assignment.Length);
        if (equals < 0)
        {
            throw new InvalidDataException("В присваивании календаря отсутствует знак равенства.");
        }

        var expression = trimmed[(equals + 1)..].Trim();
        if (!expression.EndsWith(';'))
        {
            throw new InvalidDataException("Присваивание календаря не завершено.");
        }

        var json = expression[..^1].Trim();
        return JsonSerializer.Deserialize<LegacyPayload>(json, JsonDefaults.Options)
            ?? throw new JsonException("Файл календаря пуст.");
    }

    private async Task WriteMarkerAsync(string sourceId, CancellationToken cancellationToken)
    {
        var marker = new LegacyMarker(1, sourceId, DateTimeOffset.UtcNow);
        await new AtomicFileWriter().WriteJsonAsync(_currentPaths.MigrationMarker, marker, cancellationToken).ConfigureAwait(false);
    }

    private sealed record LegacyMarker(int Version, string SourceId, DateTimeOffset ImportedAt);

    private sealed record LegacyPayload(
        [property: JsonPropertyName("updatedAt")] string? UpdatedAt,
        [property: JsonPropertyName("sourceUrl")] string? SourceUrl,
        [property: JsonPropertyName("events")] IReadOnlyList<LegacyEvent> Events);

    private sealed record LegacyEvent(
        [property: JsonPropertyName("start")] string? Start,
        [property: JsonPropertyName("end")] string? End,
        [property: JsonPropertyName("period")] string? Period,
        [property: JsonPropertyName("group")] string? Group,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("stage")] string? Stage,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("url")] string? Url);
}
