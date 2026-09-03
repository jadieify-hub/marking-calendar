using System.Globalization;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Runner;

public enum HistoryRunnerExitCode
{
    Success = 0,
    Rejected = 2,
    NetworkError = 3,
    WriteError = 4
}

public sealed record HistoryCheckOptions(string DataDirectory, bool DryRun = false, bool AcceptAnomaly = false);

public sealed record HistoryRunResult(HistoryRunnerExitCode ExitCode, string Output);

public sealed class HistoryRunner(
    IRawCalendarSource source,
    CalendarSnapshot bundledSnapshot,
    ISnapshotValidator validator,
    IEventDiffEngine diffEngine,
    IAtomicFileWriter writer,
    TimeProvider timeProvider)
{
    private static readonly Uri ChangelogUrl = new("https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md");
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);
    private readonly IRawCalendarSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly CalendarSnapshot _bundledSnapshot = bundledSnapshot ?? throw new ArgumentNullException(nameof(bundledSnapshot));
    private readonly ISnapshotValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly IEventDiffEngine _diffEngine = diffEngine ?? throw new ArgumentNullException(nameof(diffEngine));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<HistoryRunResult> CheckAsync(HistoryCheckOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        var paths = new AppPaths(options.DataDirectory, AppStorageLayout.Flat);

        try
        {
            using var store = CreateStore(paths, _validator);
            var storedBaseline = await store.LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
            var baseline = storedBaseline ?? _bundledSnapshot;
            if (storedBaseline is null && !options.DryRun)
            {
                var seeded = await store.SaveValidatedAsync(_bundledSnapshot, cancellationToken).ConfigureAwait(false);
                if (!seeded.Saved)
                {
                    return new HistoryRunResult(HistoryRunnerExitCode.Rejected, FormatRejected(seeded.Validation));
                }
            }

            var payload = await _source.FetchWithRawAsync(cancellationToken).ConfigureAwait(false);
            var validation = _validator.Validate(payload.Snapshot, baseline);
            var anomalyAccepted = options.AcceptAnomaly
                && validation.Errors.Count > 0
                && validation.Errors.All(error => error.Code == SnapshotValidationErrorCode.AnomalousCount);

            if (!validation.IsValid && !anomalyAccepted)
            {
                if (!options.DryRun)
                {
                    await WriteRejectedAsync(paths, payload.RawJson, cancellationToken).ConfigureAwait(false);
                }

                return new HistoryRunResult(HistoryRunnerExitCode.Rejected, FormatRejected(validation));
            }

            var changes = baseline.Id == payload.Snapshot.Id
                ? ChangeSet.Empty
                : _diffEngine.Compare(baseline.Events, payload.Snapshot.Events);
            var batchId = changes.HasChanges
                ? ChangeBatchIdFactory.FromSnapshots(baseline.Id, payload.Snapshot.Id)
                : null;

            if (options.DryRun)
            {
                return new HistoryRunResult(
                    HistoryRunnerExitCode.Success,
                    batchId is null ? "DRY_RUN UNCHANGED" : $"DRY_RUN CHANGED={batchId}");
            }

            if (baseline.Id != payload.Snapshot.Id)
            {
                using var acceptingStore = anomalyAccepted
                    ? CreateStore(paths, new CountAnomalyAcceptingValidator(_validator))
                    : null;
                var targetStore = acceptingStore ?? store;
                var saved = await targetStore.SaveValidatedAsync(payload.Snapshot, cancellationToken).ConfigureAwait(false);
                if (!saved.Saved)
                {
                    await WriteRejectedAsync(paths, payload.RawJson, cancellationToken).ConfigureAwait(false);
                    return new HistoryRunResult(HistoryRunnerExitCode.Rejected, FormatRejected(saved.Validation));
                }

                if (batchId is not null)
                {
                    await targetStore.AppendHistoryAsync(
                        new ChangeBatch(
                            batchId,
                            _timeProvider.GetUtcNow(),
                            changes,
                            baseline.Id,
                            payload.Snapshot.Id,
                            ChangeBatchSources.Public),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            await _writer.WriteTextAsync(Path.Combine(paths.RootDirectory, "source.json"), payload.RawJson, cancellationToken).ConfigureAwait(false);
            if (storedBaseline is null || baseline.Id != payload.Snapshot.Id)
            {
                var current = baseline.Id == payload.Snapshot.Id ? baseline : payload.Snapshot;
                var history = await store.LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
                await _writer.WriteJsonAsync(
                    Path.Combine(paths.RootDirectory, "manifest.json"),
                    new PublicHistoryManifest(
                        1,
                        _timeProvider.GetUtcNow(),
                        current.Id,
                        current.Events.Count,
                        history.Batches.Count,
                    new PublicHistoryFiles()),
                    cancellationToken).ConfigureAwait(false);
                await _writer.WriteTextAsync(
                    Path.Combine(paths.RootDirectory, "CHANGELOG.md"),
                    ChangeMarkdownFormatter.Format(history),
                    cancellationToken).ConfigureAwait(false);
                var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(MoscowOffset).DateTime);
                await _writer.WriteTextAsync(
                    Path.Combine(paths.RootDirectory, "feed.xml"),
                    AtomFeedWriter.Write(history, ChangelogUrl, today),
                    cancellationToken).ConfigureAwait(false);
            }

            var output = batchId is null
                ? "UNCHANGED"
                : $"CHANGED={batchId}{Environment.NewLine}CHANGES={changes.Total.ToString(CultureInfo.InvariantCulture)}";
            if (anomalyAccepted) output += Environment.NewLine + "ACCEPTED_ANOMALY";
            return new HistoryRunResult(HistoryRunnerExitCode.Success, output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CalendarSourceException error) when (error.Code is CalendarSourceError.NetworkFailure or CalendarSourceError.HttpFailure)
        {
            var hint = error.Code == CalendarSourceError.HttpFailure
                && error.Message.Contains("HTTP 403", StringComparison.Ordinal)
                    ? " источник блокирует запросы не из РФ, запускайте runner с российского адреса."
                    : string.Empty;
            return new HistoryRunResult(HistoryRunnerExitCode.NetworkError, $"NETWORK_ERROR: {error.Message}{hint}");
        }
        catch (CalendarSourceException error) when (error.Code == CalendarSourceError.InvalidPayload)
        {
            try
            {
                if (!options.DryRun && error.RawJson is not null)
                {
                    await WriteRejectedAsync(paths, error.RawJson, cancellationToken).ConfigureAwait(false);
                }

                return new HistoryRunResult(HistoryRunnerExitCode.Rejected, $"REJECTED: {error.Message}");
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                return new HistoryRunResult(HistoryRunnerExitCode.WriteError, $"WRITE_ERROR: {writeError.Message}");
            }
        }
        catch (HttpRequestException error)
        {
            return new HistoryRunResult(HistoryRunnerExitCode.NetworkError, $"NETWORK_ERROR: {error.Message}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new HistoryRunResult(HistoryRunnerExitCode.WriteError, $"WRITE_ERROR: {error.Message}");
        }
    }

    private CalendarStore CreateStore(AppPaths paths, ISnapshotValidator validator) =>
        new(paths, validator, _writer, maxHistoryBatches: 500);

    private async Task WriteRejectedAsync(AppPaths paths, string rawJson, CancellationToken cancellationToken)
    {
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(paths.RootDirectory, "rejected", $"rejected-{stamp}.json");
        await _writer.WriteTextAsync(path, rawJson, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatRejected(SnapshotValidationResult validation) =>
        "REJECTED: " + string.Join(' ', validation.Errors.Select(error => error.Message));

    private sealed class CountAnomalyAcceptingValidator(ISnapshotValidator inner) : ISnapshotValidator
    {
        public SnapshotValidationResult Validate(CalendarSnapshot candidate, CalendarSnapshot? baseline)
        {
            var result = inner.Validate(candidate, baseline);
            var remaining = result.Errors.Where(error => error.Code != SnapshotValidationErrorCode.AnomalousCount).ToArray();
            return remaining.Length == 0 ? SnapshotValidationResult.Valid : new SnapshotValidationResult(remaining);
        }
    }
}
