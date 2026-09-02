using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class EventDiffEngineTests
{
    [Fact]
    public void Compare_ReportsDateMoveWithoutAdditionOrRemoval()
    {
        var previous = E("2026-12-01", stage: "Старт обязательной передачи");
        var current = E("2027-06-01", stage: "Старт обязательной передачи");

        var result = new EventDiffEngine().Compare([previous], [current]);

        var move = Assert.Single(result.Moved);
        Assert.Equal(new DateOnly(2026, 12, 1), move.Previous.Start);
        Assert.Equal(new DateOnly(2027, 6, 1), move.Current.Start);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Compare_PairsExactDuplicateBeforeClosestMove()
    {
        var unchanged = E("2026-09-01", stage: "Окончание маркировки остатков");
        var oldFuture = E("2027-03-01", stage: "Окончание маркировки остатков");
        var newFuture = E("2027-06-01", stage: "Окончание маркировки остатков");

        var result = new EventDiffEngine().Compare([unchanged, oldFuture], [newFuture, unchanged]);

        var move = Assert.Single(result.Moved);
        Assert.Equal(new DateOnly(2027, 3, 1), move.Previous.Start);
        Assert.Equal(new DateOnly(2027, 6, 1), move.Current.Start);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Compare_ReportsWordingEditOnSameDateAsChanged()
    {
        var previous = E("2027-03-01", stage: "Окончание маркировки остатков", description: "Старая редакция");
        var current = E("2027-03-01", stage: "Завершение маркировки товарных остатков", description: "Новая редакция");

        var result = new EventDiffEngine().Compare([previous], [current]);

        Assert.Single(result.Changed);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Moved);
    }

    [Fact]
    public void Compare_ReportsUnmatchedEventsAsAddedAndRemoved()
    {
        var removed = E("2026-01-01", group: "Старая группа", url: "https://example.test/old");
        var added = E("2028-01-01", group: "Новая группа", stage: "Совершенно новый этап", url: "https://example.test/new");

        var result = new EventDiffEngine().Compare([removed], [added]);

        Assert.Equal(added, Assert.Single(result.Added));
        Assert.Equal(removed, Assert.Single(result.Removed));
    }

    [Fact]
    public void Compare_PairsMoveWithEditedWordingAndMarksItExplicitly()
    {
        var previous = E("2026-12-01", stage: "Старт передачи сведений через кассу", description: "Старая редакция");
        var current = E("2027-06-01", stage: "Начало передачи данных через кассу", description: "Новая редакция");

        var result = new EventDiffEngine().Compare([previous], [current]);

        var move = Assert.Single(result.Moved);
        Assert.True(move.WordingChanged);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        var summary = Assert.Single(new ChangeSummaryFactory().Create(result, 8, new DateOnly(2026, 9, 2), new HashSet<string>()).Items);
        Assert.Equal("01.12.2026 → 01.06.2027, формулировка изменена", summary.Detail);
    }

    [Fact]
    public void Compare_PairsAmbiguousWordingEditsBySimilarityThenDate()
    {
        var first = E("2026-10-01", stage: "Старт передачи сведений через кассу");
        var second = E("2027-04-01", stage: "Завершение передачи сведений через кассу");
        var firstMoved = E("2026-11-01", stage: "Начало передачи сведений через кассу");
        var secondMoved = E("2027-05-01", stage: "Окончание передачи сведений через кассу");

        var result = new EventDiffEngine().Compare([first, second], [secondMoved, firstMoved]);

        Assert.Collection(
            result.Moved.OrderBy(change => change.Previous.Start),
            change => Assert.Equal(firstMoved.Id, change.Current.Id),
            change => Assert.Equal(secondMoved.Id, change.Current.Id));
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Compare_LeavesAmbiguousPairsBelowSimilarityThresholdUnmatched()
    {
        var previous = new[]
        {
            E("2026-10-01", stage: "Регистрация участников оборота"),
            E("2027-04-01", stage: "Передача сведений через кассу")
        };
        var current = new[]
        {
            E("2026-11-01", stage: "Нанесение кодов на упаковку"),
            E("2027-05-01", stage: "Вывод продукции из оборота")
        };

        var result = new EventDiffEngine().Compare(previous, current);

        Assert.Empty(result.Moved);
        Assert.Empty(result.Changed);
        Assert.Equal(2, result.Added.Count);
        Assert.Equal(2, result.Removed.Count);
    }

    [Fact]
    public void Compare_ReportsGroupRenameBySharedUrlWithoutEventAdditionsOrRemovals()
    {
        var previous = E("2026-10-01", group: "Печатная продукция", url: "https://example.test/business/projects/books/");
        var current = E("2026-10-01", group: "Печатная продукция (завершен)", url: "https://example.test/business/projects/books/");

        var result = new EventDiffEngine().Compare([previous], [current]);

        Assert.Equal(new GroupRenamed("Печатная продукция", "Печатная продукция (завершен)"), Assert.Single(result.GroupsRenamed));
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Changed);
        Assert.Equal(0, result.Total);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void Compare_ReportsNewGroupWithCountAndFirstDate()
    {
        var current = new[]
        {
            E("2027-02-01", group: "Новая группа", url: "https://example.test/new"),
            E("2027-01-01", group: "Новая группа", stage: "Второй этап", url: "https://example.test/new")
        };

        var result = new EventDiffEngine().Compare([], current);

        Assert.Equal(new GroupChange("Новая группа", 2, new DateOnly(2027, 1, 1)), Assert.Single(result.GroupsAdded));
    }

    [Fact]
    public void Compare_DoesNotRenameGroupsWhenDominantLinksDiffer()
    {
        var previous = new[]
        {
            E("2026-10-01", group: "Медицинские изделия", stage: "Первый", url: "https://example.test/business/projects/medical_devices/"),
            E("2026-11-01", group: "Медицинские изделия", stage: "Второй", url: "https://example.test/business/projects/medical_devices/")
        };
        var current = previous.Select(item => item with
        {
            Id = item.Id + "-renamed",
            Group = "Медицинские изделия 2.0",
            Url = new Uri("https://example.test/business/projects/medproducts/")
        }).ToArray();

        var result = new EventDiffEngine().Compare(previous, current);

        Assert.Empty(result.GroupsRenamed);
        Assert.Equal(2, result.Added.Count);
        Assert.Equal(2, result.Removed.Count);
    }

    [Fact]
    public void Compare_DetectsRenameByEventsWhenOneGroupHasNoLink()
    {
        var previous = new[]
        {
            E("2026-10-01", group: "Печатная продукция", stage: "Первый", url: null),
            E("2026-11-01", group: "Печатная продукция", stage: "Второй", url: null)
        };
        var current = previous.Select(item => item with
        {
            Id = item.Id + "-renamed",
            Group = "Печатная продукция (завершен)",
            Url = new Uri("https://example.test/business/projects/books/")
        }).ToArray();

        var result = new EventDiffEngine().Compare(previous, current);

        Assert.Single(result.GroupsRenamed);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Compare_DoesNotGuessRenameWhenTwoCandidatesHaveEqualScore()
    {
        var previous = E("2026-10-01", group: "Старая", url: "https://example.test/shared");
        var current = new[]
        {
            E("2026-10-01", group: "Новая 1", url: "https://example.test/shared"),
            E("2026-10-01", group: "Новая 2", url: "https://example.test/shared")
        };

        var result = new EventDiffEngine().Compare([previous], current);

        Assert.Empty(result.GroupsRenamed);
        Assert.Single(result.GroupsRemoved);
        Assert.Equal(2, result.GroupsAdded.Count);
    }

    [Fact]
    public void Compare_NormalizedGroupWhitespaceDoesNotCreateGroupChange()
    {
        var previous = E("2026-10-01", group: "Радиоэлектроника ");
        var current = previous with { Id = previous.Id + "-trimmed", Group = "Радиоэлектроника" };

        var result = new EventDiffEngine().Compare([previous], [current]);

        Assert.Empty(result.GroupsAdded);
        Assert.Empty(result.GroupsRemoved);
        Assert.Empty(result.GroupsRenamed);
    }

    [Fact]
    public void Compare_NormalizesGroupAndTypeForTolerantPairing()
    {
        var previous = E("2026-10-01", group: "  Радиоэлектроника\u00a0 ", type: "ПОЭКЗЕМПЛЯРНЫЙ УЧЁТ", stage: "Первый этап");
        var current = E("2027-02-01", group: "радиоэлектроника", type: "поэкземплярный учет", stage: "Новая формулировка");

        var result = new EventDiffEngine().Compare([previous], [current]);

        Assert.Single(result.Moved);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    private static CalendarEvent E(
        string start,
        string group = "Стройматериалы",
        string type = "Розничная продажа",
        string stage = "Старт",
        string description = "Описание",
        string? url = "https://example.test/")
    {
        var date = DateOnly.ParseExact(start, "yyyy-MM-dd");
        var raw = string.Join('|', start, group, type, stage, description);
        return new CalendarEvent(EventId.FromCanonicalContent(raw), date, null, start, group, type, stage, description, url is null ? null : new Uri(url));
    }
}
