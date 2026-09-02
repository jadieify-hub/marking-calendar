namespace MarkingCalendar.Core.Events;

public sealed record CalendarEvent(
    string Id,
    DateOnly? Start,
    DateOnly? End,
    string Period,
    string Group,
    string Type,
    string Stage,
    string Description,
    Uri? Url);

public sealed class CalendarEventValidationException(string message) : Exception(message);

