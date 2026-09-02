using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public sealed record EventLineageEntry(
    DateTimeOffset CheckedAt,
    ChangeKind Kind,
    DateOnly? PreviousStart,
    DateOnly? PreviousEnd,
    string? PreviousStage,
    string? PreviousDescription,
    IReadOnlyList<ChangedField> ChangedFields);

public sealed record EventLineage(
    IReadOnlyList<EventLineageEntry> Entries,
    int MoveCount,
    DateTimeOffset? FirstSeen);

public sealed class EventLineageBuilder
{
    public static IReadOnlyDictionary<string, EventLineage> Build(
        ChangeHistory history,
        IReadOnlyList<CalendarEvent> current)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(current);

        var batches = history.Batches
            .OrderByDescending(batch => batch.CheckedAt)
            .Select(Index)
            .ToArray();
        var result = new Dictionary<string, EventLineage>(StringComparer.Ordinal);
        foreach (var calendarEvent in current)
        {
            result[calendarEvent.Id] = Follow(calendarEvent.Id, batches);
        }

        return result;
    }

    private static EventLineage Follow(string eventId, IReadOnlyList<IndexedBatch> batches)
    {
        var entries = new List<EventLineageEntry>();
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);
        var currentId = eventId;
        var nextBatch = 0;
        var moveCount = 0;
        DateTimeOffset? firstSeen = null;
        while (nextBatch < batches.Count && visitedIds.Add(currentId))
        {
            var matched = false;
            for (var index = nextBatch; index < batches.Count; index++)
            {
                var batch = batches[index];
                if (batch.ChangesByCurrentId.TryGetValue(currentId, out var change))
                {
                    entries.Add(new EventLineageEntry(
                        batch.CheckedAt,
                        change.Kind,
                        change.Previous.Start,
                        change.Previous.End,
                        change.Previous.Stage,
                        change.Previous.Description,
                        change.WordingChanged ? change.GetChangedFields() : []));
                    if (change.Kind == ChangeKind.Moved)
                    {
                        moveCount++;
                    }

                    currentId = change.Previous.Id;
                    nextBatch = index + 1;
                    matched = true;
                    break;
                }

                if (batch.AddedIds.Contains(currentId))
                {
                    entries.Add(new EventLineageEntry(batch.CheckedAt, ChangeKind.Added, null, null, null, null, []));
                    firstSeen = batch.CheckedAt;
                    nextBatch = batches.Count;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                break;
            }
        }

        return new EventLineage(entries, moveCount, firstSeen);
    }

    private static IndexedBatch Index(ChangeBatch batch)
    {
        var changes = new Dictionary<string, EventChange>(StringComparer.Ordinal);
        foreach (var change in batch.Changes.Moved.Concat(batch.Changes.Changed))
        {
            changes.TryAdd(change.Current.Id, change);
        }

        return new IndexedBatch(
            batch.CheckedAt,
            changes,
            batch.Changes.Added.Select(item => item.Id).ToHashSet(StringComparer.Ordinal));
    }

    private sealed record IndexedBatch(
        DateTimeOffset CheckedAt,
        IReadOnlyDictionary<string, EventChange> ChangesByCurrentId,
        IReadOnlySet<string> AddedIds);
}
