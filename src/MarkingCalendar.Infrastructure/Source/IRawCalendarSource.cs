using MarkingCalendar.Core.Snapshots;

namespace MarkingCalendar.Infrastructure.Source;

public sealed record CalendarSourcePayload(CalendarSnapshot Snapshot, string RawJson);

public interface IRawCalendarSource : ICalendarSource
{
    Task<CalendarSourcePayload> FetchWithRawAsync(CancellationToken cancellationToken);
}
