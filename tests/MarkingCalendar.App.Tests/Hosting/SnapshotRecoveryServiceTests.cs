using MarkingCalendar.App.Hosting;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class SnapshotRecoveryServiceTests
{
    [Fact]
    public async Task ResolveAsync_RestoresLatestArchiveBeforeUsingBundledSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        using var store = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter(), timeProvider: new FixedTimeProvider());
        var archived = Snapshot(Event("archive", 1), minute: 0);
        await store.SaveValidatedAsync(archived, CancellationToken.None);
        await store.SaveValidatedAsync(Snapshot(Event("current", 2), minute: 5), CancellationToken.None);
        await File.WriteAllTextAsync(paths.CurrentSnapshot, "{");
        var bundledCalls = 0;
        var service = new SnapshotRecoveryService(store, _ =>
        {
            bundledCalls++;
            return Task.FromResult(Snapshot(Event("bundled", 3), minute: 10));
        });

        var result = await service.ResolveAsync(CancellationToken.None);

        Assert.Equal(SnapshotOrigin.Archive, result.Origin);
        Assert.Equal(archived, result.Snapshot);
        Assert.Equal(archived, await store.LoadCurrentAsync(CancellationToken.None));
        Assert.Equal(0, bundledCalls);
    }

    private static CalendarSnapshot Snapshot(CalendarEvent calendarEvent, int minute) =>
        CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, minute, 0, TimeSpan.FromHours(3)),
            new Uri("https://example.test/source"),
            [calendarEvent]);

    private static CalendarEvent Event(string id, int day)
    {
        var date = new DateOnly(2026, 9, day);
        return new CalendarEvent(id, date, null, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "Группа", "Маркировка", "Старт", "Описание", null);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 5, 6, TimeSpan.Zero);
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
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
