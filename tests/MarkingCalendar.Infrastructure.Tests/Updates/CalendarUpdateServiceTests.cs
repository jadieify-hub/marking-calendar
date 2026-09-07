using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;
using MarkingCalendar.Infrastructure.Updates;

namespace MarkingCalendar.Infrastructure.Tests.Updates;

public sealed class CalendarUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsNoChangesForEqualContent()
    {
        using var fixture = await Fixture.CreateAsync(Snapshot([Event(1)]));
        using var service = fixture.ServiceFor(Snapshot([Event(1)], minute: 5));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.NoChanges, result.Status);
        Assert.Equal(0, result.Changes.Total);
        Assert.Empty((await fixture.Store.LoadHistoryAsync(CancellationToken.None)).Batches);
    }

    [Fact]
    public async Task CheckAsync_SavesChangedSnapshotAndOneHistoryBatch()
    {
        using var fixture = await Fixture.CreateAsync(Snapshot([Event(1)]));
        var candidate = Snapshot([Event(1) with { Start = new DateOnly(2027, 1, 1), Id = "moved-1" }], minute: 5);
        using var service = fixture.ServiceFor(candidate);

        var result = await service.CheckAsync(CancellationToken.None);
        var current = await fixture.Store.LoadCurrentAsync(CancellationToken.None);
        var history = await fixture.Store.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.Updated, result.Status);
        Assert.Equal(candidate, current);
        Assert.Single(history.Batches);
        Assert.Single(history.Batches[0].Changes.Moved);
        Assert.Equal(fixture.Baseline.Id, history.Batches[0].PreviousSnapshotId);
        Assert.Equal(candidate.Id, history.Batches[0].CurrentSnapshotId);
        Assert.Equal("local", history.Batches[0].Source);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(110)]
    public async Task CheckAsync_RejectsAnomalousCandidateAndKeepsBaseline(int count)
    {
        var baseline = Snapshot(Enumerable.Range(1, 432).Select(Event).ToArray());
        using var fixture = await Fixture.CreateAsync(baseline);
        var candidate = Snapshot(Enumerable.Range(1, count).Select(Event).ToArray(), minute: 5);

        var logger = new RecordingLogger();
        using var service = fixture.ServiceFor(candidate, logger);
        var result = await service.CheckAsync(CancellationToken.None);
        var current = await fixture.Store.LoadCurrentAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.Rejected, result.Status);
        Assert.Equal(baseline, current);
        Assert.Empty((await fixture.Store.LoadHistoryAsync(CancellationToken.None)).Batches);
        Assert.Contains("аномально", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Entries, entry => entry.Level == AppLogLevel.Warning && entry.Source == "calendar-update");
        Assert.Single(logger.RejectedPayloads);
    }

    [Fact]
    public async Task CheckAsync_ReturnsFailureAndKeepsBaselineOnNetworkError()
    {
        var baseline = Snapshot([Event(1)]);
        using var fixture = await Fixture.CreateAsync(baseline);
        var logger = new RecordingLogger();
        using var service = new CalendarUpdateService(new ThrowingSource(), fixture.Store, new EventDiffEngine(), new FixedTimeProvider(), logger);

        var result = await service.CheckAsync(CancellationToken.None);
        var current = await fixture.Store.LoadCurrentAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.Failed, result.Status);
        Assert.Equal(baseline, current);
        Assert.Equal("Не удалось обновить данные. Используется сохранённый календарь.", result.UserMessage);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal(AppLogLevel.Error, logged.Level);
        Assert.IsType<HttpRequestException>(logged.Exception);
    }

    [Fact]
    public async Task CheckAsync_DoesNotDuplicateHistoryForSameCandidate()
    {
        using var fixture = await Fixture.CreateAsync(Snapshot([Event(1)]));
        var candidate = Snapshot([Event(1) with { Description = "Новая редакция", Id = "changed-1" }], minute: 5);
        using var service = fixture.ServiceFor(candidate);

        await service.CheckAsync(CancellationToken.None);
        await service.CheckAsync(CancellationToken.None);
        var history = await fixture.Store.LoadHistoryAsync(CancellationToken.None);

        Assert.Single(history.Batches);
    }

    [Theory]
    [InlineData("changes.json")]
    [InlineData("current.json")]
    public async Task CheckAsync_WriteFailureKeepsBaselineAndRetrySavesOneBatch(
        string failingFile)
    {
        var baseline = Snapshot([Event(1)]);
        var writer = new FailOnceWriter(failingFile);
        using var fixture = await Fixture.CreateAsync(baseline, writer);
        var candidate = Snapshot([Event(1) with { Description = "Новая редакция", Id = "changed-1" }], minute: 5);
        using var service = fixture.ServiceFor(candidate);

        var failed = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.Failed, failed.Status);
        Assert.Equal(baseline, await fixture.Store.LoadCurrentAsync(CancellationToken.None));
        Assert.Empty((await fixture.Store.LoadHistoryAsync(CancellationToken.None)).Batches);

        var retried = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(CalendarUpdateStatus.Updated, retried.Status);
        Assert.Equal(candidate, await fixture.Store.LoadCurrentAsync(CancellationToken.None));
        Assert.Single((await fixture.Store.LoadHistoryAsync(CancellationToken.None)).Batches);
    }
    private static CalendarSnapshot Snapshot(IReadOnlyList<CalendarEvent> events, int minute = 0) =>
        CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 10, minute, 0, TimeSpan.FromHours(3)), new Uri("https://example.test/source"), events);

    private static CalendarEvent Event(int index)
    {
        var date = new DateOnly(2026, 9, 1).AddDays(index);
        return new CalendarEvent($"event-{index}", date, null, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), $"Группа {index}", "Маркировка", "Старт", "Описание", null);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, CalendarSnapshot baseline, CalendarStore store)
        {
            Root = root;
            Baseline = baseline;
            Store = store;
        }

        public string Root { get; }
        public CalendarSnapshot Baseline { get; }
        public CalendarStore Store { get; }

        public static async Task<Fixture> CreateAsync(CalendarSnapshot baseline, IAtomicFileWriter? writer = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            using (var seedStore = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter()))
            {
                await seedStore.SaveValidatedAsync(baseline, CancellationToken.None);
            }

            var store = new CalendarStore(paths, new SnapshotValidator(), writer ?? new AtomicFileWriter());
            return new Fixture(root, baseline, store);
        }

        public CalendarUpdateService ServiceFor(CalendarSnapshot candidate, IAppLogger? logger = null) =>
            new(new FixedSource(candidate), Store, new EventDiffEngine(), new FixedTimeProvider(), logger);

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FixedSource(CalendarSnapshot snapshot) : ICalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class ThrowingSource : ICalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 5, 0, TimeSpan.Zero);
    }

    private sealed class FailOnceWriter(string failingFile) : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        private bool _failed;

        public async Task WriteJsonAsync<T>(string destination, T value, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(destination);
            if (!_failed && fileName.Equals(failingFile, StringComparison.OrdinalIgnoreCase))
            {
                _failed = true;
                throw new IOException($"Failed to write {fileName}.");
            }

            await _inner.WriteJsonAsync(destination, value, cancellationToken);
        }

        public Task WriteTextAsync(string destination, string value, CancellationToken cancellationToken) =>
            _inner.WriteTextAsync(destination, value, cancellationToken);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(AppLogLevel Level, string Source, string Message, Exception? Exception)> Entries { get; } = [];
        public List<(string Source, string Json)> RejectedPayloads { get; } = [];

        public void Log(AppLogLevel level, string source, string message, Exception? exception = null) =>
            Entries.Add((level, source, message, exception));

        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken)
        {
            RejectedPayloads.Add((source, json));
            return Task.CompletedTask;
        }
    }
}
