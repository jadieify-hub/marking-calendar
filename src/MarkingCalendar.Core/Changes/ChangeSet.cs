using System.Text.Json.Serialization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public sealed record GroupChange(string Name, int EventCount, DateOnly? FirstDate);
public sealed record GroupRenamed(string From, string To);

public sealed record ChangeSet
{
    [JsonConstructor]
    public ChangeSet(
        IReadOnlyList<CalendarEvent>? added,
        IReadOnlyList<CalendarEvent>? removed,
        IReadOnlyList<EventChange>? moved,
        IReadOnlyList<EventChange>? changed,
        IReadOnlyList<GroupChange>? groupsAdded = null,
        IReadOnlyList<GroupChange>? groupsRemoved = null,
        IReadOnlyList<GroupRenamed>? groupsRenamed = null)
    {
        Added = added ?? [];
        Removed = removed ?? [];
        Moved = moved ?? [];
        Changed = changed ?? [];
        GroupsAdded = groupsAdded ?? [];
        GroupsRemoved = groupsRemoved ?? [];
        GroupsRenamed = groupsRenamed ?? [];
    }

    public IReadOnlyList<CalendarEvent> Added { get; }
    public IReadOnlyList<CalendarEvent> Removed { get; }
    public IReadOnlyList<EventChange> Moved { get; }
    public IReadOnlyList<EventChange> Changed { get; }
    public IReadOnlyList<GroupChange> GroupsAdded { get; }
    public IReadOnlyList<GroupChange> GroupsRemoved { get; }
    public IReadOnlyList<GroupRenamed> GroupsRenamed { get; }
    public int Total => Added.Count + Removed.Count + Moved.Count + Changed.Count;
    public int GroupTotal => GroupsAdded.Count + GroupsRemoved.Count + GroupsRenamed.Count;
    public bool HasChanges => Total > 0 || GroupTotal > 0;

    public static ChangeSet Empty { get; } = new([], [], [], []);
}
