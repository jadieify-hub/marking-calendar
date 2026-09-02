namespace MarkingCalendar.Core.Events;

public interface IEventNormalizer
{
    CalendarEvent Normalize(SourceEvent source);
}

