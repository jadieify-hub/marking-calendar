namespace MarkingCalendar.Core.Events;

public sealed record SourceEvent(
    string? DateStart,
    string? DateEnd,
    string? Period,
    string? Group,
    string? Type,
    string? Stage,
    string? Description,
    string? Url);

