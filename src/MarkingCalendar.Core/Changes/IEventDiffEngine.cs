using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public interface IEventDiffEngine
{
    ChangeSet Compare(IReadOnlyList<CalendarEvent> previous, IReadOnlyList<CalendarEvent> current);
}

