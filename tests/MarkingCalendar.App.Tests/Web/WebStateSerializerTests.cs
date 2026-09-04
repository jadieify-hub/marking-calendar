using MarkingCalendar.App.Web;

namespace MarkingCalendar.App.Tests.Web;

public sealed class WebStateSerializerTests
{
    [Fact]
    public void Serialize_PreservesNullFieldsRequiredByTypeScriptContract()
    {
        var model = new AppViewModel(
            "02.09.2026, 10:00",
            1,
            "2026-09-02",
            [new ProductGroupViewModel("игрушки", "Игрушки", 1)],
            [],
            false,
            "auto",
            [new CategoryViewModel("retail", "Розничная продажа", "#1f93bb", "#3fbde4")],
            [new CalendarEventViewModel("1", "2026-09-01", null, "", "Игрушки", "Розничная продажа", "Розничная продажа", "Старт", "", null, "retail", null, 0, [])],
            [],
            null,
            new ChangeHistoryViewModel([], 0),
            new AppStatusViewModel("ready", "Данные актуальны"),
            null,
            null,
            new AppUpdateViewModel("current", "Установлена последняя версия", null, null, false),
            new ProductViewModel("Календарь маркировки", "0.1.5", "Руслан Керусов", "KRS", "https://github.com/jadieify-hub/marking-calendar", "https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md", "https://pay.cloudtips.ru/p/a18da555", "Независимый проект"),
            []);

        var json = WebStateSerializer.Serialize(model);

        Assert.Contains("\"updateNotice\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"toast\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"progress\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"end\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"url\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"changeNotificationsEnabled\":true", json, StringComparison.Ordinal);
    }
}
