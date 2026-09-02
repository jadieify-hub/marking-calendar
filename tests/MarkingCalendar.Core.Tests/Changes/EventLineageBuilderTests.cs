using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class EventLineageBuilderTests
{
    [Fact]
    public void Build_FollowsThreeMovesFromNewestToOldest()
    {
        var original = E("2026-03-01", "Первый этап");
        var first = E("2026-06-01", "Первый этап");
        var second = E("2026-09-01", "Уточнённый этап");
        var current = E("2027-01-15", "Уточнённый этап");
        var history = new ChangeHistory([
            Batch("newest", "2026-08-01", new ChangeSet([], [], [EventChange.Moved(second, current)], [])),
            Batch("middle", "2026-06-01", new ChangeSet([], [], [EventChange.Moved(first, second)], [])),
            Batch("oldest", "2026-04-01", new ChangeSet([], [], [EventChange.Moved(original, first)], []))
        ]);

        var lineage = Assert.Single(EventLineageBuilder.Build(history, [current])).Value;

        Assert.Equal(3, lineage.MoveCount);
        Assert.Null(lineage.FirstSeen);
        Assert.Collection(
            lineage.Entries,
            entry => { Assert.Equal(new DateOnly(2026, 9, 1), entry.PreviousStart); Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), entry.CheckedAt); },
            entry => { Assert.Equal(new DateOnly(2026, 6, 1), entry.PreviousStart); Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), entry.CheckedAt); },
            entry => { Assert.Equal(new DateOnly(2026, 3, 1), entry.PreviousStart); Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), entry.CheckedAt); });
    }

    [Fact]
    public void Build_ReturnsEmptyLineageForEventWithoutHistory()
    {
        var current = E("2026-09-01", "Старт");

        var lineage = Assert.Single(EventLineageBuilder.Build(ChangeHistory.Empty, [current])).Value;

        Assert.Empty(lineage.Entries);
        Assert.Equal(0, lineage.MoveCount);
        Assert.Null(lineage.FirstSeen);
    }

    [Fact]
    public void Build_UsesAddedEntryAsFirstSeen()
    {
        var current = E("2026-09-01", "Старт");
        var checkedAt = new DateTimeOffset(2026, 7, 15, 12, 30, 0, TimeSpan.Zero);
        var history = new ChangeHistory([new ChangeBatch("added", checkedAt, new ChangeSet([current], [], [], []))]);

        var lineage = Assert.Single(EventLineageBuilder.Build(history, [current])).Value;

        Assert.Equal(checkedAt, lineage.FirstSeen);
        var entry = Assert.Single(lineage.Entries);
        Assert.Equal(ChangeKind.Added, entry.Kind);
        Assert.Null(entry.PreviousStart);
    }

    [Fact]
    public void Build_DoesNotCountTextOnlyChangeAsMove()
    {
        var previous = E("2026-09-01", "Старая формулировка");
        var current = E("2026-09-01", "Новая формулировка");
        var history = new ChangeHistory([
            Batch("changed", "2026-08-20", new ChangeSet([], [], [], [EventChange.Changed(previous, current)]))
        ]);

        var lineage = Assert.Single(EventLineageBuilder.Build(history, [current])).Value;

        Assert.Equal(0, lineage.MoveCount);
        Assert.Equal(ChangeKind.Changed, Assert.Single(lineage.Entries).Kind);
    }

    private static ChangeBatch Batch(string id, string checkedAt, ChangeSet changes) =>
        new(id, new DateTimeOffset(DateOnly.ParseExact(checkedAt, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), changes);

    private static CalendarEvent E(string start, string stage)
    {
        var date = DateOnly.ParseExact(start, "yyyy-MM-dd");
        return new CalendarEvent(
            EventId.FromCanonicalContent($"{start}|{stage}"),
            date,
            null,
            "Период",
            "Игрушки",
            "Розничная продажа",
            stage,
            $"Описание {stage}",
            null);
    }
}
