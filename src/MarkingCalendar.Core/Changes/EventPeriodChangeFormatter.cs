using System.Globalization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public static class EventPeriodChangeFormatter
{
    public static string Format(EventChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Previous.Start == change.Current.Start && change.Previous.End != change.Current.End)
        {
            return $"Окончание: {FormatDate(change.Previous.End)} → {FormatDate(change.Current.End)}";
        }

        if (change.Previous.Start != change.Current.Start && change.Previous.End != change.Current.End)
        {
            return $"Период: {FormatRange(change.Previous)} → {FormatRange(change.Current)}";
        }

        var previousDate = EventDate(change.Previous);
        var currentDate = EventDate(change.Current);
        if (previousDate != currentDate)
        {
            return $"{FormatDate(previousDate)} → {FormatDate(currentDate)}";
        }

        var previousRange = FormatRange(change.Previous);
        var currentRange = FormatRange(change.Current);
        return !string.Equals(previousRange, currentRange, StringComparison.Ordinal)
            ? $"Период: {previousRange} → {currentRange}"
            : $"Дата: {FormatDate(currentDate)}";
    }

    private static string FormatRange(CalendarEvent item)
    {
        if (item.Start is not null && item.End is not null)
        {
            return item.Start == item.End
                ? FormatDate(item.Start)
                : $"{FormatDate(item.Start)}–{FormatDate(item.End)}";
        }

        if (item.Start is not null) return $"с {FormatDate(item.Start)}";
        if (item.End is not null) return $"до {FormatDate(item.End)}";
        return "дата не указана";
    }

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "дата не указана";

    private static DateOnly? EventDate(CalendarEvent item) => item.Start ?? item.End;
}
