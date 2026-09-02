using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Runner;

namespace MarkingCalendar.Runner.Tests;

public sealed class TelegramAnnouncementRendererTests
{
    [Fact]
    public void Render_LimitsMessageAndReportsOmittedEvents()
    {
        var events = Enumerable.Range(1, 50)
            .Select(index => new CalendarEvent(
                $"event-{index}",
                new DateOnly(2026, 10, 1),
                null,
                "с 1 октября 2026 года",
                $"Очень длинное название товарной группы {index} {new string('я', 120)}",
                "Обязательная маркировка",
                $"Подробное описание этапа {new string('ю', 180)}",
                "Описание",
                null))
            .ToArray();
        var batch = new ChangeBatch(
            "batch-1",
            new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero),
            new ChangeSet(events, [], [], []));

        var text = TelegramAnnouncementRenderer.Render(batch);

        Assert.True(text.Length <= 3500, $"Длина сообщения: {text.Length}");
        Assert.Contains("и ещё ", text, StringComparison.Ordinal);
        Assert.StartsWith("Календарь маркировки — изменения", text, StringComparison.Ordinal);
    }
}
