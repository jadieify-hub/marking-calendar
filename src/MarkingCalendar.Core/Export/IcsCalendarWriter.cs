using System.Globalization;
using System.Text;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Export;

public sealed class IcsCalendarWriter(string productName, string productVersion, TimeProvider timeProvider)
{
    private const string NewLine = "\r\n";
    private readonly string _productName = productName ?? throw new ArgumentNullException(nameof(productName));
    private readonly string _productVersion = productVersion ?? throw new ArgumentNullException(nameof(productVersion));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public string Write(IEnumerable<CalendarEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var result = new StringBuilder();
        Append(result, "BEGIN:VCALENDAR");
        Append(result, "VERSION:2.0");
        Append(result, $"PRODID:-//KRS//{_productName} {_productVersion}//RU");
        Append(result, "CALSCALE:GREGORIAN");
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        foreach (var item in events)
        {
            var start = item.Start ?? item.End ?? throw new ArgumentException("Событие не содержит даты.", nameof(events));
            var end = (item.End ?? item.Start ?? start).AddDays(1);
            Append(result, "BEGIN:VEVENT");
            Append(result, $"UID:{Escape(item.Id)}@marking-calendar");
            Append(result, $"DTSTAMP:{stamp}");
            Append(result, $"DTSTART;VALUE=DATE:{Date(start)}");
            Append(result, $"DTEND;VALUE=DATE:{Date(end)}");
            Append(result, $"SUMMARY:{Escape($"{item.Group} — {item.Stage}")}");
            Append(result, $"DESCRIPTION:{Escape(Description(item))}");
            if (item.Url is not null) Append(result, $"URL:{item.Url.AbsoluteUri}");
            Append(result, "END:VEVENT");
        }

        Append(result, "END:VCALENDAR");
        return result.ToString();
    }

    private static string Description(CalendarEvent item) =>
        string.IsNullOrWhiteSpace(item.Description)
            ? $"Период: {item.Period}"
            : $"{item.Description}\n\nПериод: {item.Period}";

    private static string Date(DateOnly value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal);

    private static void Append(StringBuilder target, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > 75)
            {
                target.Append(NewLine).Append(' ');
                octets = 1;
            }
            target.Append(rune);
            octets += rune.Utf8SequenceLength;
        }
        target.Append(NewLine);
    }
}
