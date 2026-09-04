using System.Diagnostics;
using System.Globalization;
using MarkingCalendar.App.Web;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Web;

public sealed class AppViewModelFactoryTests
{
    [Fact]
    public void Create_AssignsCategoryColorsAndFormatsDatesInHost()
    {
        var calendarEvent = new CalendarEvent(
            "event-1",
            new DateOnly(2026, 9, 1),
            null,
            "с 1 сентября 2026",
            "Игрушки",
            "Розничная продажа",
            "Старт",
            "Описание",
            new Uri("https://честныйзнак.рф/source"));
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, 45, 0, TimeSpan.FromHours(3)),
            new Uri("https://честныйзнак.рф/source"),
            [calendarEvent]);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var toast = new ToastViewModel("error", "Не удалось скопировать ссылку.");
        var result = factory.Create(
            snapshot,
            ChangeHistory.Empty,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            null,
            toast: toast,
            archives: [new SnapshotArchiveInfo("archive.json", new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(3)))],
            comparison: new SnapshotComparison(
                new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(3)),
                new ChangeSummaryResult(new ChangeCounts(0, 0, 0, 0), [], 0, 0)));

        Assert.Equal(
            [
                ("retail", "#1f93bb", "#3fbde4"),
                ("edo", "#7b4fd0", "#a583f0"),
                ("ban", "#cf4842", "#ec7069"),
                ("permit", "#b8801d", "#e0aa4a"),
                ("marking", "#1e9a63", "#3fc98a"),
                ("registration", "#3d72bd", "#6ea3ef"),
                ("other", "#6b7783", "#8e9aa7")
            ],
            result.Categories.Select(item => (item.Id, item.Color, item.ColorDark)));
        var item = Assert.Single(result.Events);
        Assert.Equal("2026-09-01", item.Start);
        Assert.Equal("retail", item.Category);
        Assert.Equal("Розничная продажа", item.TypeLabel);
        Assert.Equal("2026-09-02", result.Today);
        Assert.Equal("02.09.2026, 10:45", result.UpdatedAt);
        Assert.Equal("Руслан Керусов", result.About.Developer);
        Assert.Equal("KRS", result.About.Publisher);
        Assert.Equal("https://github.com/jadieify-hub/marking-calendar", result.About.RepositoryUrl);
        Assert.True(result.About.ChangeNotificationsEnabled);
        Assert.Equal("https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md", result.About.HistoryUrl);
        Assert.Equal("https://pay.cloudtips.ru/p/a18da555", result.About.SupportUrl);
        Assert.Contains("Независимый проект", result.About.Disclaimer);
        Assert.True(result.About.PublicHistoryEnabled);
        Assert.Same(toast, result.Toast);
        Assert.Equal(new ArchiveViewModel("archive.json", "01.08.2026, 10:00"), Assert.Single(result.Archives));
        Assert.Equal("01.08.2026, 10:00", result.Comparison?.BaseRetrievedAt);
    }

    [Fact]
    public void Create_BuildsOrderedGroupCountsAndRestoresKnownPreferences()
    {
        var events = new[]
        {
            Event("1", "Яйца"),
            Event("2", "Аптеки"),
            Event("3", "Яйца")
        };
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, 45, 0, TimeSpan.FromHours(3)),
            new Uri("https://честныйзнак.рф/source"),
            events);
        var state = new AppState(2, [], ["Яйца", "Удалённая группа"], "dark");
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var result = factory.Create(
            snapshot,
            ChangeHistory.Empty,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            state);

        Assert.Collection(
            result.Groups,
            group => { Assert.Equal("аптеки", group.Key); Assert.Equal("Аптеки", group.Name); Assert.Equal(1, group.EventCount); },
            group => { Assert.Equal("яйца", group.Key); Assert.Equal("Яйца", group.Name); Assert.Equal(2, group.EventCount); });
        Assert.Equal(["яйца"], result.SelectedGroups);
        Assert.True(result.HasSelectedGroups);
        Assert.Equal("dark", result.Theme);
    }

    [Theory]
    [InlineData("2026-07-04T07:00:00Z", true)]
    [InlineData("2026-07-03T07:00:00Z", false)]
    public void Create_MarksGroupsNewForSixtyDays(string checkedAt, bool expected)
    {
        var item = Event("1", "Игрушки");
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test"),
            [item]);
        var history = new ChangeHistory([
            new ChangeBatch(
                "groups",
                DateTimeOffset.Parse(checkedAt, CultureInfo.InvariantCulture),
                new ChangeSet([], [], [], [], groupsAdded: [new GroupChange("Игрушки", 1, item.Start)]))
        ]);

        var result = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider()).Create(
            snapshot,
            history,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            AppState.Initial);

        Assert.Equal(expected, Assert.Single(result.Groups).IsNew);
        Assert.Equal(expected ? 1 : 0, result.GroupSuggestions.Count);
    }

    [Fact]
    public void Create_OrdersCompletedGroupsLastAndDoesNotSuggestThem()
    {
        var completed = Event("1", "Архивная группа");
        var active = Event("2", "Яркая группа");
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test"),
            [completed, active]);
        var history = new ChangeHistory([
            new ChangeBatch(
                "groups",
                new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.Zero),
                new ChangeSet([], [], [], [], groupsAdded: [
                    new GroupChange(completed.Group, 1, completed.Start),
                    new GroupChange(active.Group, 1, active.Start)
                ]))
        ]);
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("home", "Для дома")],
            [new(completed.Group, "/old/", ["home"], "completed"), new(active.Group, "/active/", ["home"], GoodsPath: false)]);

        var result = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider()).Create(
            snapshot,
            history,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            AppState.Initial,
            groupMap: map);

        Assert.Equal(["Яркая группа", "Архивная группа"], result.Groups.Select(group => group.Name));
        Assert.False(result.Groups[0].HasGoodsPage);
        Assert.True(result.Groups[^1].HasGoodsPage);
        Assert.True(result.Groups[^1].IsCompleted);
        Assert.Equal("Яркая группа", Assert.Single(result.GroupSuggestions).Name);
    }

    [Theory]
    [InlineData("", true, false, false, true, "Новая группа в календаре")]
    [InlineData("food", true, false, false, true, "Новая группа в вашей отрасли")]
    [InlineData("pharma", true, false, false, false, null)]
    [InlineData("food", false, false, false, true, "Новая группа в календаре")]
    [InlineData("food", true, true, false, false, null)]
    [InlineData("food", true, false, true, false, null)]
    public void Create_TargetsNewGroupSuggestionsBySector(
        string selectedSector,
        bool groupIsMapped,
        bool alreadySelected,
        bool hidden,
        bool expected,
        string? expectedMessage)
    {
        var item = Event("new", "Новая группа");
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test"),
            [item]);
        var history = new ChangeHistory([
            new ChangeBatch(
                "new-group",
                new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.Zero),
                new ChangeSet([], [], [], [], groupsAdded: [new GroupChange(item.Group, 1, item.Start)]))
        ]);
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            groupIsMapped ? [new(item.Group, "/new/", ["food"])] : []);
        var state = AppState.Initial.WithProfile(
            [],
            selectedSector.Length == 0 ? [] : [selectedSector],
            new Dictionary<string, bool>(),
            alreadySelected ? [item.Group] : []);
        if (hidden) state = state.WithHiddenGroupSuggestions([item.Group]);

        var result = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider()).Create(
            snapshot,
            history,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            state,
            groupMap: map);

        Assert.Equal(expected, result.GroupSuggestions.Count == 1);
        if (expected) Assert.Equal(expectedMessage, Assert.Single(result.GroupSuggestions).Message);
    }

    [Fact]
    public void Create_RecalculatesSuggestionsAfterSectorChanges()
    {
        var item = Event("new", "Новая группа");
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test"),
            [item]);
        var history = new ChangeHistory([
            new ChangeBatch(
                "new-group",
                new DateTimeOffset(2026, 9, 1, 7, 0, 0, TimeSpan.Zero),
                new ChangeSet([], [], [], [], groupsAdded: [new GroupChange(item.Group, 1, item.Start)]))
        ]);
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            [new(item.Group, "/new/", ["food"])]);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var food = factory.Create(snapshot, history, new AppStatusViewModel("ready", "Данные актуальны"), null,
            AppState.Initial.WithProfile([], ["food"], new Dictionary<string, bool>(), []), groupMap: map);
        var pharma = factory.Create(snapshot, history, new AppStatusViewModel("ready", "Данные актуальны"), null,
            AppState.Initial.WithProfile([], ["pharma"], new Dictionary<string, bool>(), []), groupMap: map);

        Assert.Single(food.GroupSuggestions);
        Assert.Empty(pharma.GroupSuggestions);
    }

    [Fact]
    public void Create_PreservesSectorOrderAndCountsOnlyActiveGroups()
    {
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test"),
            [Event("1", "БАД"), Event("2", "Печатная продукция")]);
        var map = new GroupMap(
            2,
            "2026-09-02",
            [new("food", "Продукты"), new("pharma", "Аптека")],
            [new("БАД", "/bad/", ["food", "pharma"]), new("Печатная продукция", "/books/", ["food"], "completed")]);
        var state = AppState.Initial.WithProfile(["retail", "producer"], ["pharma"], new Dictionary<string, bool>(), ["бад"]);

        var result = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider()).Create(
            snapshot,
            ChangeHistory.Empty,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            state,
            groupMap: map);

        Assert.Collection(
            result.Profile.Sectors,
            sector => { Assert.Equal("food", sector.Id); Assert.Equal(1, sector.ActiveGroupCount); },
            sector => { Assert.Equal("pharma", sector.Id); Assert.Equal(1, sector.ActiveGroupCount); });
        Assert.Equal(["retail", "producer"], result.Profile.SelectedRoles);
        Assert.Equal(["pharma"], result.Profile.SelectedSectors);
        Assert.Equal(["retail", "edo", "ban", "permit", "marking", "registration"], result.Profile.RoleCategories);
        Assert.True(result.Profile.OnboardingCompleted);
    }

    [Theory]
    [InlineData("Розничная продажа", "Розничная продажа")]
    [InlineData("Поэкземплярный учет по ЭДО", "Поэкземплярный учёт")]
    [InlineData("Объемно-сортовой учет по ЭДО", "Объёмно-сортовой учёт")]
    [InlineData("Партионный учет по ЭДО", "Партионный учёт")]
    [InlineData("Вывод из оборота по иным причинам", "Вывод из оборота")]
    [InlineData("Запрет оборота немаркированной продукции", "Запрет оборота")]
    [InlineData("Разрешительный режим", "Разрешительный режим")]
    [InlineData("Обязательная маркировка (ввод в оборот)", "Ввод в оборот")]
    [InlineData("Маркировка остатков", "Маркировка остатков")]
    [InlineData("Эксперимент", "Эксперимент")]
    [InlineData("Обязательная регистрация", "Регистрация")]
    public void Create_ProvidesStableShortTypeLabels(string type, string expected)
    {
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, 45, 0, TimeSpan.FromHours(3)),
            new Uri("https://честныйзнак.рф/source"),
            [Event("1", "Игрушки", type)]);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var result = factory.Create(
            snapshot,
            ChangeHistory.Empty,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            AppState.Initial);

        Assert.Equal(expected, Assert.Single(result.Events).TypeLabel);
    }

    [Fact]
    public void Create_ComputesUnreadBatchesOnceFromSeenIds()
    {
        var calendarEvent = new CalendarEvent("event-1", new DateOnly(2026, 9, 1), null, "01.09.2026", "Игрушки", "Розничная продажа", "Старт", "", null);
        var snapshot = CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), new Uri("https://example.test"), [calendarEvent]);
        var changes = new ChangeSet([calendarEvent], [], [], []);
        var history = new ChangeHistory([
            new ChangeBatch("batch-new", new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), changes),
            new ChangeBatch("batch-seen", new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(3)), changes)
        ]);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var result = factory.Create(
            snapshot,
            history,
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            AppState.Initial.WithSeen(["batch-seen"]));

        Assert.Equal(1, result.History.UnreadCount);
        Assert.True(result.History.Batches[0].IsUnread);
        Assert.False(result.History.Batches[1].IsUnread);
    }

    [Fact]
    public void Create_ShowsExplicitLocalNoticeEvenWhenPublicBatchWasAlreadySeen()
    {
        var item = Event("event", "Игрушки");
        var snapshot = CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), new Uri("https://example.test"), [item]);
        var batch = new ChangeBatch("shared", new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.FromHours(3)), new ChangeSet([item], [], [], []));
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var result = factory.Create(
            snapshot,
            new ChangeHistory([batch]),
            new AppStatusViewModel("updated", "Календарь обновлён"),
            batch,
            AppState.Initial.WithSeen([batch.Id]),
            noticeRelatedBatchIds: ["public-1", "public-2"]);

        Assert.NotNull(result.UpdateNotice);
        Assert.Equal(["public-1", "public-2"], result.UpdateNotice.RelatedBatchIds);
        Assert.Equal(0, result.History.UnreadCount);
    }

    [Theory]
    [InlineData("2026-08-03T12:00:00+00:00", true)]
    [InlineData("2026-06-04T12:00:00+00:00", false)]
    [InlineData("2026-09-03T12:00:00+00:00", false)]
    public void Create_ExposesRecentChangeOnlyWithinSixtyDays(string checkedAt, bool expectedRecent)
    {
        var previous = Event("previous", "Игрушки") with
        {
            Id = "previous",
            Start = new DateOnly(2026, 9, 1),
            Stage = "Старая формулировка"
        };
        var current = previous with
        {
            Id = "current",
            Start = new DateOnly(2027, 1, 15),
            Stage = "Новая формулировка"
        };
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, 45, 0, TimeSpan.FromHours(3)),
            new Uri("https://честныйзнак.рф/source"),
            [current]);
        var history = new ChangeHistory([
            new ChangeBatch("move", DateTimeOffset.Parse(checkedAt, System.Globalization.CultureInfo.InvariantCulture),
                new ChangeSet([], [], [EventChange.Moved(previous, current)], []))
        ]);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var result = factory.Create(
            snapshot,
            history,
            new AppStatusViewModel("ready", "Данные актуальны"),
            history.Batches[0],
            AppState.Initial.WithGroups(["Игрушки"]));

        var item = Assert.Single(result.Events);
        Assert.Equal(1, item.MoveCount);
        var entry = Assert.Single(item.History);
        Assert.Equal("moved", entry.Kind);
        Assert.Equal("2026-09-01", entry.PreviousStart);
        Assert.Equal("Старая формулировка", entry.PreviousStage);
        Assert.Equal(
            new ChangedFieldViewModel("stage", "Старая формулировка", "Новая формулировка"),
            Assert.Single(entry.ChangedFields));
        Assert.Equal(
            new ChangedFieldViewModel("stage", "Старая формулировка", "Новая формулировка"),
            Assert.Single(Assert.Single(result.UpdateNotice!.Items).ChangedFields));
        Assert.Equal(1, result.UpdateNotice.MineCount);
        Assert.Equal(0, result.UpdateNotice.OthersCount);
        Assert.True(Assert.Single(result.UpdateNotice.Items).Mine);
        Assert.Equal(expectedRecent, item.RecentChange is not null);
    }

    [Fact]
    public void Create_HandlesFullCalendarAndRetainedHistoryWithinSoftBudget()
    {
        var events = Enumerable.Range(1, 432)
            .Select(index => Event($"current-{index}", $"Группа {index % 54:00}") with
            {
                Start = new DateOnly(2026 + index / 360, index % 12 + 1, index % 27 + 1)
            })
            .ToArray();
        var history = new ChangeHistory(Enumerable.Range(0, 50).Select(batchIndex =>
            new ChangeBatch(
                $"batch-{batchIndex}",
                new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)).AddDays(-batchIndex),
                new ChangeSet(
                    Enumerable.Range(0, 30).Select(itemIndex => Event($"history-{batchIndex}-{itemIndex}", $"Архив {itemIndex:00}")).ToArray(),
                    [],
                    [],
                    []))).ToArray());
        var snapshot = CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)),
            new Uri("https://честныйзнак.рф/source"),
            events);
        var factory = new AppViewModelFactory(new ChangeSummaryFactory(), new FixedTimeProvider());

        var stopwatch = Stopwatch.StartNew();
        var result = factory.Create(snapshot, history, new AppStatusViewModel("ready", "Данные актуальны"), null, AppState.Initial);
        stopwatch.Stop();

        Assert.Equal(432, result.Events.Count);
        Assert.Equal(50, result.History.Batches.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200), $"Формирование view model заняло {stopwatch.Elapsed.TotalMilliseconds:F1} мс.");
    }

    private static CalendarEvent Event(string id, string group, string type = "Розничная продажа") => new(
        id,
        new DateOnly(2026, 9, 1),
        null,
        "с 1 сентября 2026",
        group,
        type,
        "Старт",
        "Описание",
        null);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test-Moscow", TimeSpan.FromHours(3), "Test-Moscow", "Test-Moscow");
    }
}
