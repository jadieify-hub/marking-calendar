using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;
using MarkingCalendar.Runner;
using System.Text.Json;

namespace MarkingCalendar.Runner.Tests;

public sealed class HistoryRunnerTests
{
    [Fact]
    public async Task CheckAsync_SeedsBundledBaselineOnFirstRun()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var runner = CreateRunner(baseline, baseline);

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Success, result.ExitCode);
        Assert.Equal("UNCHANGED", result.Output);
        Assert.True(File.Exists(Path.Combine(temp.Path, "current.json")));
        Assert.Equal("{\"source\":\"fixture\"}", await File.ReadAllTextAsync(Path.Combine(temp.Path, "source.json")));
        var manifest = await ReadManifestAsync(temp.Path);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(baseline.Id, manifest.SnapshotId);
        Assert.Equal(120, manifest.EventCount);
        Assert.Equal(0, manifest.BatchCount);
        Assert.Equal("groups.json", manifest.GroupsUrl);
        Assert.Equal("history/changes.json", manifest.Files.History);
        Assert.Contains("# История изменений календаря маркировки", await File.ReadAllTextAsync(Path.Combine(temp.Path, "CHANGELOG.md")), StringComparison.Ordinal);
        Assert.Contains("<feed", await File.ReadAllTextAsync(Path.Combine(temp.Path, "feed.xml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_ChangedSnapshotCreatesOneBatchWithSnapshotIds()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var candidate = Snapshot(baseline.Events.Select((item, index) => index == 0
            ? item with { Start = item.Start?.AddDays(1), Id = "moved-event" }
            : item).ToArray(), minute: 5);
        await CreateRunner(baseline, baseline).CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var runner = CreateRunner(baseline, candidate);

        var first = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var second = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var paths = new AppPaths(temp.Path, AppStorageLayout.Flat);
        using var store = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter(), maxHistoryBatches: 500);
        var batch = Assert.Single((await store.LoadHistoryAsync(CancellationToken.None)).Batches);

        Assert.Equal(HistoryRunnerExitCode.Success, first.ExitCode);
        Assert.StartsWith("CHANGED=", first.Output, StringComparison.Ordinal);
        Assert.Contains("CHANGES=1", first.Output, StringComparison.Ordinal);
        Assert.Equal("UNCHANGED", second.Output);
        Assert.Equal(baseline.Id, batch.PreviousSnapshotId);
        Assert.Equal(candidate.Id, batch.CurrentSnapshotId);
        Assert.Equal("public", batch.Source);
        Assert.Equal(1, (await ReadManifestAsync(temp.Path)).BatchCount);
    }

    [Fact]
    public async Task CheckAsync_UnchangedRunDoesNotRegenerateManifest()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        await CreateRunner(baseline, baseline, new FixedTimeProvider(7)).CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        await CreateRunner(baseline, baseline, new FixedTimeProvider(8)).CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 9, 2, 7, 10, 0, TimeSpan.Zero), (await ReadManifestAsync(temp.Path)).GeneratedAt);
    }

    [Fact]
    public async Task CheckAsync_RejectsAnomalousSnapshotWithoutChangingCurrent()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var candidate = Snapshot(Enumerable.Range(1, 10).Select(Event).ToArray(), minute: 5);
        await CreateRunner(baseline, baseline).CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var runner = CreateRunner(baseline, candidate);

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var current = await ReadCurrentAsync(temp.Path);

        Assert.Equal(HistoryRunnerExitCode.Rejected, result.ExitCode);
        Assert.Equal(baseline.Id, current.Id);
        Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "rejected"), "*.json"));
    }

    [Fact]
    public async Task CheckAsync_FirstRejectedFetchStillSeedsBundledBaseline()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var candidate = Snapshot(Enumerable.Range(1, 10).Select(Event).ToArray(), minute: 5);
        var runner = CreateRunner(baseline, candidate);

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Rejected, result.ExitCode);
        Assert.Equal(baseline.Id, (await ReadCurrentAsync(temp.Path)).Id);
    }

    [Fact]
    public async Task CheckAsync_DryRunDoesNotWriteFiles()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var candidate = Snapshot(baseline.Events.Skip(1).ToArray(), minute: 5);
        var runner = CreateRunner(baseline, candidate);

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path, DryRun: true), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Success, result.ExitCode);
        Assert.Contains("DRY_RUN", result.Output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task CheckAsync_AcceptsOnlyCountAnomalyWhenExplicitlyRequested()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var candidate = Snapshot(Enumerable.Range(1, 10).Select(Event).ToArray(), minute: 5);
        await CreateRunner(baseline, baseline).CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);
        var runner = CreateRunner(baseline, candidate);

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path, AcceptAnomaly: true), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Success, result.ExitCode);
        Assert.Equal(candidate.Id, (await ReadCurrentAsync(temp.Path)).Id);
        Assert.Contains("ACCEPTED_ANOMALY", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_MapsNetworkFailureToExitCodeThree()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var runner = new HistoryRunner(
            new ThrowingSource(),
            baseline,
            new SnapshotValidator(),
            new EventDiffEngine(),
            new AtomicFileWriter(),
            new FixedTimeProvider());

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.NetworkError, result.ExitCode);
        Assert.StartsWith("NETWORK_ERROR:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_InvalidSourcePayloadIsRejectedAndPreserved()
    {
        using var temp = new TemporaryDirectory();
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var runner = new HistoryRunner(
            new InvalidSource(),
            baseline,
            new SnapshotValidator(),
            new EventDiffEngine(),
            new AtomicFileWriter(),
            new FixedTimeProvider());

        var result = await runner.CheckAsync(new HistoryCheckOptions(temp.Path), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Rejected, result.ExitCode);
        var rejected = Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "rejected"), "*.json"));
        Assert.Equal("{\"data\":{}}", await File.ReadAllTextAsync(rejected));
    }

    [Fact]
    public async Task CheckAsync_MapsFileFailureToExitCodeFour()
    {
        using var temp = new TemporaryDirectory();
        var dataFile = Path.Combine(temp.Path, "not-a-directory");
        await File.WriteAllTextAsync(dataFile, "occupied");
        var baseline = Snapshot(Enumerable.Range(1, 120).Select(Event).ToArray());
        var runner = CreateRunner(baseline, baseline);

        var result = await runner.CheckAsync(new HistoryCheckOptions(dataFile), CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.WriteError, result.ExitCode);
        Assert.StartsWith("WRITE_ERROR:", result.Output, StringComparison.Ordinal);
    }

    private static HistoryRunner CreateRunner(CalendarSnapshot bundled, CalendarSnapshot candidate, TimeProvider? timeProvider = null) => new(
        new FixedSource(candidate),
        bundled,
        new SnapshotValidator(),
        new EventDiffEngine(),
        new AtomicFileWriter(),
        timeProvider ?? new FixedTimeProvider());

    private static async Task<PublicHistoryManifest> ReadManifestAsync(string root)
    {
        await using var stream = File.OpenRead(Path.Combine(root, "manifest.json"));
        return Assert.IsType<PublicHistoryManifest>(
            await JsonSerializer.DeserializeAsync<PublicHistoryManifest>(stream, JsonDefaults.Options));
    }

    private static async Task<CalendarSnapshot> ReadCurrentAsync(string root)
    {
        using var store = new CalendarStore(
            new AppPaths(root, AppStorageLayout.Flat),
            new SnapshotValidator(),
            new AtomicFileWriter(),
            maxHistoryBatches: 500);
        return Assert.IsType<CalendarSnapshot>(await store.LoadCurrentAsync(CancellationToken.None));
    }

    private static CalendarSnapshot Snapshot(IReadOnlyList<CalendarEvent> events, int minute = 0) =>
        CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 7, minute, 0, TimeSpan.Zero), new Uri("https://example.test/source"), events);

    private static CalendarEvent Event(int index)
    {
        var date = new DateOnly(2026, 9, 1).AddDays(index);
        return new CalendarEvent($"event-{index}", date, null, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), $"Группа {index}", "Маркировка", "Старт", "Описание", null);
    }

    private sealed class FixedSource(CalendarSnapshot snapshot) : IRawCalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public Task<CalendarSourcePayload> FetchWithRawAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CalendarSourcePayload(snapshot, "{\"source\":\"fixture\"}"));
    }

    private sealed class ThrowingSource : IRawCalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) =>
            throw new CalendarSourceException(CalendarSourceError.NetworkFailure, "offline");

        public Task<CalendarSourcePayload> FetchWithRawAsync(CancellationToken cancellationToken) =>
            throw new CalendarSourceException(CalendarSourceError.NetworkFailure, "offline");
    }

    private sealed class InvalidSource : IRawCalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) =>
            throw new CalendarSourceException(CalendarSourceError.InvalidPayload, "invalid", rawJson: "{\"data\":{}}");

        public Task<CalendarSourcePayload> FetchWithRawAsync(CancellationToken cancellationToken) =>
            throw new CalendarSourceException(CalendarSourceError.InvalidPayload, "invalid", rawJson: "{\"data\":{}}");
    }

    private sealed class FixedTimeProvider(int hour = 7) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, hour, 10, 0, TimeSpan.Zero);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkingCalendar.Runner.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
