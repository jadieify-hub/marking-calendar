using MarkingCalendar.Core.Snapshots;

namespace MarkingCalendar.Infrastructure.Source;

public interface ICalendarSource
{
    Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken);
}

