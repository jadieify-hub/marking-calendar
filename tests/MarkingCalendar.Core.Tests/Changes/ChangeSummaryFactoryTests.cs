using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class ChangeSummaryFactoryTests
{
    [Fact]
    public void Create_FormatsMoveAndPreservesAllCounts()
    {
        var previous = E("2026-09-01", "Игрушки");
        var current = E("2027-03-01", "Игрушки");
        var set = new ChangeSet([], [], [EventChange.Moved(previous, current)], []);

        var result = new ChangeSummaryFactory().Create(set, 8, new DateOnly(2026, 9, 2), new HashSet<string>());

        var summary = Assert.Single(result.Items);
        Assert.Equal(ChangeKind.Moved, summary.Kind);
        Assert.Equal("01.09.2026 → 01.03.2027", summary.Detail);
        Assert.Equal(1, result.Counts.Moved);
        Assert.Equal(1, result.Counts.Total);
    }

    [Fact]
    public void Create_LimitsItemsToEightAndSortsNearestFutureFirst()
    {
        var added = Enumerable.Range(1, 12)
            .Select(day => E($"2026-10-{day:00}", $"Группа {day:00}"))
            .Reverse()
            .ToArray();
        var set = new ChangeSet(added, [], [], []);

        var result = new ChangeSummaryFactory().Create(set, 8, new DateOnly(2026, 9, 2), new HashSet<string>());

        Assert.Equal(8, result.Items.Count);
        Assert.Equal("Группа 01 — Розничная продажа", result.Items[0].Title);
        Assert.Equal(12, result.Counts.Added);
        Assert.Equal(12, result.Counts.Total);
    }

    [Fact]
    public void Create_ListsChangedTextFieldsForChangedAndMovedEventsOnly()
    {
        var previous = E("2026-09-01", "Игрушки") with
        {
            Stage = "Старый этап",
            Description = "Старое описание",
            Period = "Старый период",
            Url = new Uri("https://example.test/old")
        };
        var changed = previous with
        {
            Id = "changed",
            Stage = "Новый этап",
            Description = "Новое описание"
        };
        var moved = previous with
        {
            Id = "moved",
            Start = new DateOnly(2026, 10, 1),
            Period = "Новый период",
            Url = new Uri("https://example.test/new")
        };
        var added = E("2026-11-01", "Обувь");
        var set = new ChangeSet([added], [], [EventChange.Moved(previous, moved)], [EventChange.Changed(previous, changed)]);

        var result = new ChangeSummaryFactory().Create(set, 8, new DateOnly(2026, 9, 2), new HashSet<string>());

        var movedSummary = Assert.Single(result.Items, item => item.Kind == ChangeKind.Moved);
        Assert.Collection(
            movedSummary.ChangedFields,
            field => Assert.Equal(("period", "Старый период", "Новый период"), (field.Field, field.Previous, field.Current)),
            field => Assert.Equal(("url", "https://example.test/old", "https://example.test/new"), (field.Field, field.Previous, field.Current)));
        var changedSummary = Assert.Single(result.Items, item => item.Kind == ChangeKind.Changed);
        Assert.Collection(
            changedSummary.ChangedFields,
            field => Assert.Equal(("stage", "Старый этап", "Новый этап"), (field.Field, field.Previous, field.Current)),
            field => Assert.Equal(("description", "Старое описание", "Новое описание"), (field.Field, field.Previous, field.Current)));
        Assert.Empty(Assert.Single(result.Items, item => item.Kind == ChangeKind.Added).ChangedFields);
    }

    [Fact]
    public void Create_PrioritizesSelectedGroupsAndReportsMineAndOtherCounts()
    {
        var mine = E("2027-10-01", "Моя группа");
        var other = E("2026-09-03", "Другая группа");
        var set = new ChangeSet([other, mine], [], [], []);

        var result = new ChangeSummaryFactory().Create(
            set,
            8,
            new DateOnly(2026, 9, 2),
            new HashSet<string>(["моя ГРУППА"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(1, result.MineCount);
        Assert.Equal(1, result.OthersCount);
        Assert.Equal([true, false], result.Items.Select(item => item.Mine));
        Assert.Equal("Моя группа — Розничная продажа", result.Items[0].Title);
    }

    [Fact]
    public void Create_PrioritizesRoleCategoriesWhenDatesAreEqual()
    {
        var retail = E("2026-09-03", "А группа");
        var marking = E("2026-09-03", "Я группа") with { Type = "Обязательная маркировка (ввод в оборот)" };
        var set = new ChangeSet([retail, marking], [], [], []);

        var result = new ChangeSummaryFactory().Create(
            set,
            8,
            new DateOnly(2026, 9, 2),
            new HashSet<string>(),
            new HashSet<EventCategory> { EventCategory.Marking });

        Assert.StartsWith("Я группа", result.Items[0].Title, StringComparison.Ordinal);
    }

    private static CalendarEvent E(string start, string group)
    {
        var date = DateOnly.ParseExact(start, "yyyy-MM-dd");
        return new CalendarEvent(EventId.FromCanonicalContent(start + group), date, null, "Период", group, "Розничная продажа", "Старт", "", null);
    }
}
