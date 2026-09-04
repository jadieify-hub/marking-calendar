using System.Text;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Export;

namespace MarkingCalendar.Core.Tests.Export;

public sealed class IcsCalendarWriterTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 9, 4, 10, 11, 12, TimeSpan.Zero);

    [Fact]
    public void Write_OneEvent_MatchesCalendarSnapshot()
    {
        var actual = Writer().Write([Event("event-1", new DateOnly(2026, 9, 1), null)]);

        Assert.Equal(
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "PRODID:-//KRS//Календарь маркировки 0.1.10//RU\r\n" +
            "CALSCALE:GREGORIAN\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:event-1@marking-calendar\r\n" +
            "DTSTAMP:20260904T101112Z\r\n" +
            "DTSTART;VALUE=DATE:20260901\r\n" +
            "DTEND;VALUE=DATE:20260902\r\n" +
            "SUMMARY:Бакалея — Старт маркировки\r\n" +
            "DESCRIPTION:Текст\\n\\nПериод: с 1 сентября\r\n" +
            "URL:https://честныйзнак.рф/business/projects/grocery/\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n",
            actual);
    }

    [Fact]
    public void Write_TwoEvents_PreservesInputOrder()
    {
        var actual = Writer().Write([
            Event("second", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 4), group: "Обувь"),
            Event("first", new DateOnly(2026, 9, 1), null, group: "Бакалея")]);

        Assert.Equal(
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "PRODID:-//KRS//Календарь маркировки 0.1.10//RU\r\n" +
            "CALSCALE:GREGORIAN\r\n" +
            EventSnapshot("second", "20261002", "20261005", "Обувь") +
            EventSnapshot("first", "20260901", "20260902", "Бакалея") +
            "END:VCALENDAR\r\n",
            actual);
    }

    [Fact]
    public void Write_EscapesTextFields()
    {
        var item = Event("id,1", new DateOnly(2026, 9, 1), null) with
        {
            Group = "Группа, один; два\\три",
            Stage = "Строка 1\r\nСтрока 2",
            Description = "Описание, часть; путь\\файл\nпродолжение",
            Period = "с 1, сентября; без переноса"
        };

        var actual = Writer().Write([item]).Replace("\r\n ", "", StringComparison.Ordinal);

        Assert.Contains("UID:id\\,1@marking-calendar\r\n", actual, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Группа\\, один\\; два\\\\три — Строка 1\\nСтрока 2\r\n", actual, StringComparison.Ordinal);
        Assert.Contains("DESCRIPTION:Описание\\, часть\\; путь\\\\файл\\nпродолжение\\n\\nПериод: с 1\\, сентября\\; без переноса\r\n", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_FoldsLongCyrillicLinesAtSeventyFiveUtf8Octets()
    {
        var item = Event("long", new DateOnly(2026, 9, 1), null) with
        {
            Description = string.Concat(Enumerable.Repeat("длинная кириллическая строка ", 8))
        };

        var actual = Writer().Write([item]);
        var physicalLines = actual.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.All(physicalLines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Line is {Encoding.UTF8.GetByteCount(line)} octets: {line}"));
        Assert.Contains(physicalLines, line => line.StartsWith(' '));
    }

    private static IcsCalendarWriter Writer() =>
        new("Календарь маркировки", "0.1.10", new FixedTimeProvider());

    private static CalendarEvent Event(string id, DateOnly start, DateOnly? end, string group = "Бакалея") =>
        new(id, start, end, "с 1 сентября", group, "Маркировка", "Старт маркировки", "Текст", new Uri("https://честныйзнак.рф/business/projects/grocery/"));

    private static string EventSnapshot(string id, string start, string end, string group) =>
        "BEGIN:VEVENT\r\n" +
        $"UID:{id}@marking-calendar\r\n" +
        "DTSTAMP:20260904T101112Z\r\n" +
        $"DTSTART;VALUE=DATE:{start}\r\n" +
        $"DTEND;VALUE=DATE:{end}\r\n" +
        $"SUMMARY:{group} — Старт маркировки\r\n" +
        "DESCRIPTION:Текст\\n\\nПериод: с 1 сентября\r\n" +
        "URL:https://честныйзнак.рф/business/projects/grocery/\r\n" +
        "END:VEVENT\r\n";

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Stamp;
    }
}
