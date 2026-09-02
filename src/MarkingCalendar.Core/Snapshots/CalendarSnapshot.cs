using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Snapshots;

public sealed record CalendarSnapshot(
    string Id,
    DateTimeOffset RetrievedAt,
    Uri SourceUrl,
    IReadOnlyList<CalendarEvent> Events)
{
    public static CalendarSnapshot Create(
        DateTimeOffset retrievedAt,
        Uri sourceUrl,
        IReadOnlyList<CalendarEvent> events)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        ArgumentNullException.ThrowIfNull(events);
        var canonical = string.Join('|', events.Select(item => item.Id).Order(StringComparer.Ordinal));
        return new CalendarSnapshot(EventId.FromCanonicalContent(canonical), retrievedAt, sourceUrl, events.ToArray());
    }

    public bool Equals(CalendarSnapshot? other) =>
        other is not null
        && Id == other.Id
        && RetrievedAt == other.RetrievedAt
        && SourceUrl == other.SourceUrl
        && Events.SequenceEqual(other.Events);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(RetrievedAt);
        hash.Add(SourceUrl);
        foreach (var item in Events)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
