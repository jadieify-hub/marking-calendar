using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;
using System.Text.Json;

namespace MarkingCalendar.Infrastructure.Updates;

public sealed class CalendarUpdateService(
    ICalendarSource source,
    CalendarStore store,
    IEventDiffEngine diffEngine,
    TimeProvider timeProvider,
    IAppLogger? logger = null)
    : IDisposable
{
    private readonly ICalendarSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly CalendarStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IEventDiffEngine _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IAppLogger? _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CalendarUpdateResult> CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await _store.LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
            var candidate = await _source.FetchAsync(cancellationToken).ConfigureAwait(false);
            if (baseline?.Id == candidate.Id)
            {
                _logger?.Log(AppLogLevel.Debug, "calendar-update", "Источник не содержит изменений.");
                return new CalendarUpdateResult(
                    CalendarUpdateStatus.NoChanges,
                    baseline,
                    ChangeSet.Empty,
                    null,
                    "Данные актуальны.");
            }

            var changes = baseline is null
                ? new ChangeSet(candidate.Events, [], [], [])
                : _diffEngine.Compare(baseline.Events, candidate.Events);
            var saved = await _store.SaveValidatedAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!saved.Saved)
            {
                var message = string.Join(' ', saved.Validation.Errors.Select(error => error.Message));
                _logger?.Log(AppLogLevel.Warning, "calendar-update", $"Полученный снимок отклонён: {message}");
                if (_logger is not null)
                {
                    var rejectedJson = JsonSerializer.Serialize(candidate, JsonDefaults.Options);
                    await _logger.SaveRejectedJsonAsync("calendar-update", rejectedJson, cancellationToken).ConfigureAwait(false);
                }

                return new CalendarUpdateResult(
                    CalendarUpdateStatus.Rejected,
                    baseline,
                    ChangeSet.Empty,
                    null,
                    message);
            }

            ChangeBatch? batch = null;
            if (changes.HasChanges)
            {
                var batchId = ChangeBatchIdFactory.FromSnapshots(baseline?.Id, candidate.Id);
                batch = new ChangeBatch(
                    batchId,
                    _timeProvider.GetUtcNow(),
                    changes,
                    baseline?.Id,
                    candidate.Id,
                    ChangeBatchSources.Local);
                await _store.AppendHistoryAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            var updatedMessage = changes.Total == 0 ? "Календарь обновлён." : $"Календарь обновлён: {changes.Total} изменений.";
            _logger?.Log(AppLogLevel.Info, "calendar-update", updatedMessage);
            return new CalendarUpdateResult(
                CalendarUpdateStatus.Updated,
                candidate,
                changes,
                batch,
                updatedMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "calendar-update", "Не удалось обновить данные.", error);
            var baseline = await SafeLoadBaselineAsync().ConfigureAwait(false);
            return new CalendarUpdateResult(
                CalendarUpdateStatus.Failed,
                baseline,
                ChangeSet.Empty,
                null,
                "Не удалось обновить данные. Используется сохранённый календарь.",
                error);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Core.Snapshots.CalendarSnapshot?> SafeLoadBaselineAsync()
    {
        try
        {
            return await _store.LoadCurrentAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
