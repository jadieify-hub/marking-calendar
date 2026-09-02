using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;

namespace MarkingCalendar.Core.Tests.Groups;

public sealed class GroupMapTests
{
    [Fact]
    public void Validate_ReportsDuplicateNormalizedNameAndUnknownSector()
    {
        var map = Map(
            [new("Игрушки", "/business/projects/toys/", ["home"]),
             new(" игрушки ", "/business/projects/toys-2/", ["missing"])]);

        var errors = GroupMapValidator.Validate(map);

        Assert.Contains(errors, error => error.Contains("имя", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_UsesLinkBeforeNameAndReportsNameConflict()
    {
        var map = Map([new("Печатная продукция (завершен)", "/business/projects/books/", ["home"], "completed")]);
        var events = new[]
        {
            Event("Печатная продукция", "https://честныйзнак.рф/business/projects/books/")
        };

        var result = GroupMapMatcher.Match(map, events);

        var match = Assert.Single(result.Matches);
        Assert.Equal("Печатная продукция (завершен)", match.Entry.Name);
        Assert.True(match.NameConflict);
        Assert.Empty(result.SnapshotOnly);
        Assert.Empty(result.MapOnly);
    }

    [Fact]
    public void Match_ReportsSnapshotAndMapGroupsMissingOnTheOtherSide()
    {
        var map = Map([
            new("Игрушки", "/business/projects/toys/", ["home"]),
            new("Завершённая группа", "/business/projects/old/", ["home"], "completed")
        ]);
        var events = new[]
        {
            Event("Игрушки", "https://честныйзнак.рф/business/projects/toys/"),
            Event("Новая группа", "https://честныйзнак.рф/business/projects/new/")
        };

        var result = GroupMapMatcher.Match(map, events);

        Assert.Equal(["Новая группа"], result.SnapshotOnly);
        Assert.Equal(["Завершённая группа"], result.MapOnly);
    }

    [Fact]
    public void GroupSelection_RecalculatesSectorUnionAndKeepsManualOverrides()
    {
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            [new("БАД", "/bad/", ["food", "pharma"]), new("Лекарства", "/med/", ["pharma"])]);
        var manual = new Dictionary<string, bool>();

        var both = GroupSelectionCalculator.Calculate(map, ["food", "pharma"], manual);
        var foodOnly = GroupSelectionCalculator.Calculate(map, ["food"], manual);
        var none = GroupSelectionCalculator.Calculate(map, [], manual);
        var manuallyIncluded = GroupSelectionCalculator.Calculate(map, [], new Dictionary<string, bool> { ["бад"] = true });

        Assert.Contains("бад", both);
        Assert.Contains("бад", foodOnly);
        Assert.DoesNotContain("бад", none);
        Assert.Contains("бад", manuallyIncluded);
    }

    [Fact]
    public void GroupSelection_ManualExclusionOverridesEverySelectedSector()
    {
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            [new("БАД", "/bad/", ["food", "pharma"])]);

        var selected = GroupSelectionCalculator.Calculate(
            map,
            ["food", "pharma"],
            new Dictionary<string, bool> { ["бад"] = false });

        Assert.DoesNotContain("бад", selected);
    }

    [Fact]
    public void GroupSelection_CapturesManualDifferencesFromSectorDefaults()
    {
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            [new("БАД", "/bad/", ["food"]), new("Лекарства", "/med/", ["pharma"])]);

        var overrides = GroupSelectionCalculator.CaptureOverrides(map, ["food"], ["лекарства"]);

        Assert.False(overrides["бад"]);
        Assert.True(overrides["лекарства"]);
    }

    private static GroupMap Map(IReadOnlyList<GroupMapEntry> groups) => new(
        2,
        "2026-09-02",
        [new GroupSector("home", "Товары для дома")],
        groups);

    private static CalendarEvent Event(string group, string url) => new(
        EventId.FromCanonicalContent(group + url),
        new DateOnly(2026, 9, 2),
        null,
        "02.09.2026",
        group,
        "Регистрация",
        "Старт",
        "Описание",
        new Uri(url));
}
