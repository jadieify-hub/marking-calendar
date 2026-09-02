using MarkingCalendar.Core.Events;
using System.Globalization;

namespace MarkingCalendar.Core.Changes;

public sealed class EventDiffEngine : IEventDiffEngine
{
    public ChangeSet Compare(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var groupChanges = DetectGroupChanges(previous, current);
        var aliases = groupChanges.Renamed.ToDictionary(
            item => GroupKey.Normalize(item.From),
            item => GroupKey.Normalize(item.To),
            StringComparer.Ordinal);

        var usedPrevious = new HashSet<int>();
        var usedCurrent = new HashSet<int>();
        var moved = new List<EventChange>();
        var changed = new List<EventChange>();

        PairExact(previous, current, aliases, usedPrevious, usedCurrent);
        PairByIdentity(previous, current, aliases, usedPrevious, usedCurrent, moved, changed);
        PairWordingEdits(previous, current, aliases, usedPrevious, usedCurrent, changed);
        PairTolerantWordingEdits(previous, current, aliases, usedPrevious, usedCurrent, moved, changed);

        var added = current.Where((_, index) => !usedCurrent.Contains(index)).ToArray();
        var removed = previous.Where((_, index) => !usedPrevious.Contains(index)).ToArray();
        return new ChangeSet(
            added,
            removed,
            moved.ToArray(),
            changed.ToArray(),
            groupChanges.Added,
            groupChanges.Removed,
            groupChanges.Renamed);
    }

    private static void PairExact(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<int> usedPrevious,
        HashSet<int> usedCurrent)
    {
        var byContent = previous
            .Select((item, index) => (item, index))
            .GroupBy(pair => ContentKey(pair.item, CanonicalGroup(pair.item, true, aliases)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<int>(group.Select(pair => pair.index)), StringComparer.Ordinal);

        for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            if (!byContent.TryGetValue(ContentKey(current[currentIndex], CanonicalGroup(current[currentIndex], false, aliases)), out var indexes) || indexes.Count == 0)
            {
                continue;
            }

            usedPrevious.Add(indexes.Dequeue());
            usedCurrent.Add(currentIndex);
        }
    }

    private static void PairByIdentity(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<int> usedPrevious,
        HashSet<int> usedCurrent,
        List<EventChange> moved,
        List<EventChange> changed)
    {
        var identities = previous.Where((_, index) => !usedPrevious.Contains(index)).Select(item => Identity(item, true, aliases))
            .Concat(current.Where((_, index) => !usedCurrent.Contains(index)).Select(item => Identity(item, false, aliases)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var identity in identities)
        {
            var previousIndexes = previous.Select((item, index) => (item, index))
                .Where(pair => !usedPrevious.Contains(pair.index) && Identity(pair.item, true, aliases) == identity)
                .Select(pair => pair.index)
                .ToList();
            var currentIndexes = current.Select((item, index) => (item, index))
                .Where(pair => !usedCurrent.Contains(pair.index) && Identity(pair.item, false, aliases) == identity)
                .Select(pair => pair.index)
                .ToList();

            while (previousIndexes.Count > 0 && currentIndexes.Count > 0)
            {
                var pair = ClosestPair(previousIndexes, currentIndexes, previous, current);
                var oldEvent = previous[pair.Previous];
                var newEvent = current[pair.Current];

                usedPrevious.Add(pair.Previous);
                usedCurrent.Add(pair.Current);
                previousIndexes.Remove(pair.Previous);
                currentIndexes.Remove(pair.Current);

                if (oldEvent.Start != newEvent.Start || oldEvent.End != newEvent.End)
                {
                    moved.Add(EventChange.Moved(oldEvent, newEvent));
                }
                else if (ContentKey(oldEvent, CanonicalGroup(oldEvent, true, aliases))
                    != ContentKey(newEvent, CanonicalGroup(newEvent, false, aliases)))
                {
                    changed.Add(EventChange.Changed(oldEvent, newEvent));
                }
            }
        }
    }

    private static void PairWordingEdits(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<int> usedPrevious,
        HashSet<int> usedCurrent,
        List<EventChange> changed)
    {
        for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            if (usedCurrent.Contains(currentIndex))
            {
                continue;
            }

            var candidate = current[currentIndex];
            var previousIndex = Enumerable.Range(0, previous.Count).FirstOrDefault(
                index => !usedPrevious.Contains(index)
                    && CanonicalGroup(previous[index], true, aliases) == CanonicalGroup(candidate, false, aliases)
                    && Same(previous[index].Type, candidate.Type)
                    && previous[index].Start == candidate.Start
                    && previous[index].End == candidate.End,
                -1);

            if (previousIndex < 0)
            {
                continue;
            }

            usedPrevious.Add(previousIndex);
            usedCurrent.Add(currentIndex);
            changed.Add(EventChange.Changed(previous[previousIndex], candidate));
        }
    }

    private static (int Previous, int Current) ClosestPair(
        IReadOnlyList<int> previousIndexes,
        IReadOnlyList<int> currentIndexes,
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current)
    {
        return previousIndexes
            .SelectMany(previousIndex => currentIndexes.Select(currentIndex => new
            {
                Previous = previousIndex,
                Current = currentIndex,
                Distance = DateDistance(previous[previousIndex], current[currentIndex])
            }))
            .OrderBy(pair => pair.Distance)
            .ThenBy(pair => pair.Previous)
            .ThenBy(pair => pair.Current)
            .Select(pair => (pair.Previous, pair.Current))
            .First();
    }

    private static void PairTolerantWordingEdits(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<int> usedPrevious,
        HashSet<int> usedCurrent,
        List<EventChange> moved,
        List<EventChange> changed)
    {
        var keys = previous.Where((_, index) => !usedPrevious.Contains(index)).Select(item => GroupTypeKey(item, true, aliases))
            .Concat(current.Where((_, index) => !usedCurrent.Contains(index)).Select(item => GroupTypeKey(item, false, aliases)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var key in keys)
        {
            var previousIndexes = previous.Select((item, index) => (item, index))
                .Where(pair => !usedPrevious.Contains(pair.index) && GroupTypeKey(pair.item, true, aliases) == key)
                .Select(pair => pair.index)
                .ToList();
            var currentIndexes = current.Select((item, index) => (item, index))
                .Where(pair => !usedCurrent.Contains(pair.index) && GroupTypeKey(pair.item, false, aliases) == key)
                .Select(pair => pair.index)
                .ToList();

            while (previousIndexes.Count > 0 && currentIndexes.Count > 0)
            {
                (int Previous, int Current)? selected = previousIndexes.Count == 1 && currentIndexes.Count == 1
                    ? (previousIndexes[0], currentIndexes[0])
                    : BestWordingPair(previousIndexes, currentIndexes, previous, current);
                if (selected is null)
                {
                    break;
                }

                var pair = selected.Value;
                var oldEvent = previous[pair.Previous];
                var newEvent = current[pair.Current];
                usedPrevious.Add(pair.Previous);
                usedCurrent.Add(pair.Current);
                previousIndexes.Remove(pair.Previous);
                currentIndexes.Remove(pair.Current);
                if (oldEvent.Start != newEvent.Start || oldEvent.End != newEvent.End)
                {
                    moved.Add(EventChange.Moved(oldEvent, newEvent));
                }
                else
                {
                    changed.Add(EventChange.Changed(oldEvent, newEvent));
                }
            }
        }
    }

    private static (int Previous, int Current)? BestWordingPair(
        IReadOnlyList<int> previousIndexes,
        IReadOnlyList<int> currentIndexes,
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current)
    {
        var pair = previousIndexes
            .SelectMany(previousIndex => currentIndexes.Select(currentIndex => new
            {
                Previous = previousIndex,
                Current = currentIndex,
                Similarity = WordSimilarity(previous[previousIndex].Stage, current[currentIndex].Stage),
                Distance = DateDistance(previous[previousIndex], current[currentIndex])
            }))
            .Where(candidate => candidate.Similarity >= 0.5)
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Previous)
            .ThenBy(candidate => candidate.Current)
            .FirstOrDefault();
        return pair is null ? null : (pair.Previous, pair.Current);
    }

    private static double WordSimilarity(string left, string right)
    {
        var leftWords = Words(left);
        var rightWords = Words(right);
        if (leftWords.Count == 0 || rightWords.Count == 0)
        {
            return 0;
        }

        return (double)leftWords.Intersect(rightWords, StringComparer.Ordinal).Count()
            / Math.Max(leftWords.Count, rightWords.Count);
    }

    private static HashSet<string> Words(string value)
    {
        var normalized = Normalize(value);
        var characters = normalized.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray();
        return new string(characters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int DateDistance(CalendarEvent left, CalendarEvent right)
    {
        var leftDate = left.Start ?? left.End;
        var rightDate = right.Start ?? right.End;
        return leftDate is null || rightDate is null
            ? int.MaxValue
            : Math.Abs(rightDate.Value.DayNumber - leftDate.Value.DayNumber);
    }

    private static GroupChanges DetectGroupChanges(
        IReadOnlyList<CalendarEvent> previous,
        IReadOnlyList<CalendarEvent> current)
    {
        var previousGroups = BuildGroups(previous);
        var currentGroups = BuildGroups(current);
        var removed = previousGroups.Keys.Except(currentGroups.Keys, StringComparer.Ordinal).ToArray();
        var added = currentGroups.Keys.Except(previousGroups.Keys, StringComparer.Ordinal).ToArray();
        var candidates = removed
            .SelectMany(oldKey => added.Select(newKey => CreateRenameCandidate(previousGroups[oldKey], currentGroups[newKey])))
            .OfType<RenameCandidate>()
            .ToArray();

        var bestByOld = candidates
            .GroupBy(candidate => candidate.Old.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var bestScore = group.Max(candidate => candidate.Score);
                var best = group.Where(candidate => candidate.Score == bestScore).ToArray();
                return best.Length == 1 ? best[0] : null;
            })
            .OfType<RenameCandidate>()
            .ToArray();
        var selected = bestByOld
            .GroupBy(candidate => candidate.New.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var bestScore = group.Max(candidate => candidate.Score);
                var best = group.Where(candidate => candidate.Score == bestScore).ToArray();
                return best.Length == 1 ? best[0] : null;
            })
            .OfType<RenameCandidate>()
            .ToArray();
        var renamedOld = selected.Select(candidate => candidate.Old.Key).ToHashSet(StringComparer.Ordinal);
        var renamedNew = selected.Select(candidate => candidate.New.Key).ToHashSet(StringComparer.Ordinal);

        return new GroupChanges(
            added.Where(key => !renamedNew.Contains(key)).Select(key => Change(currentGroups[key])).OrderBy(item => item.Name, StringComparer.CurrentCulture).ToArray(),
            removed.Where(key => !renamedOld.Contains(key)).Select(key => Change(previousGroups[key])).OrderBy(item => item.Name, StringComparer.CurrentCulture).ToArray(),
            selected.Select(candidate => new GroupRenamed(candidate.Old.Name, candidate.New.Name)).OrderBy(item => item.From, StringComparer.CurrentCulture).ToArray());
    }

    private static Dictionary<string, GroupBucket> BuildGroups(IReadOnlyList<CalendarEvent> events) => events
        .GroupBy(item => GroupKey.Normalize(item.Group), StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group =>
            {
                var items = group.ToArray();
                var dominantUrl = items
                    .Select(item => item.Url?.AbsoluteUri)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .GroupBy(url => url!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(urls => urls.Count())
                    .ThenBy(urls => urls.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(urls => urls.Key)
                    .FirstOrDefault();
                return new GroupBucket(
                    group.Key,
                    items[0].Group.Replace('\u00a0', ' ').Trim(),
                    items,
                    dominantUrl);
            },
            StringComparer.Ordinal);

    private static RenameCandidate? CreateRenameCandidate(GroupBucket oldGroup, GroupBucket newGroup)
    {
        var oldKeys = oldGroup.Events.GroupBy(TypeStageKey).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var newKeys = newGroup.Events.GroupBy(TypeStageKey).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var matches = oldKeys.Sum(pair => newKeys.TryGetValue(pair.Key, out var count) ? Math.Min(pair.Value, count) : 0);
        var smallerCount = Math.Min(oldGroup.Events.Count, newGroup.Events.Count);
        var score = smallerCount == 0 ? 0 : (double)matches / smallerCount;
        if (oldGroup.DominantUrl is not null && newGroup.DominantUrl is not null)
        {
            return oldGroup.DominantUrl.Equals(newGroup.DominantUrl, StringComparison.OrdinalIgnoreCase)
                ? new RenameCandidate(oldGroup, newGroup, score)
                : null;
        }

        return score > 0.5 ? new RenameCandidate(oldGroup, newGroup, score) : null;
    }

    private static string TypeStageKey(CalendarEvent item) =>
        string.Join('|', Normalize(item.Type), Normalize(item.Stage));

    private static GroupChange Change(GroupBucket group) => new(
        group.Name,
        group.Events.Count,
        group.Events.Select(item => item.Start ?? item.End).Where(date => date is not null).Min());

    private static string Identity(
        CalendarEvent item,
        bool previous,
        IReadOnlyDictionary<string, string> aliases) =>
        string.Join('|', CanonicalGroup(item, previous, aliases), Normalize(item.Type), Normalize(item.Stage));

    private static string GroupTypeKey(
        CalendarEvent item,
        bool previous,
        IReadOnlyDictionary<string, string> aliases) =>
        string.Join('|', CanonicalGroup(item, previous, aliases), Normalize(item.Type));

    private static string ContentKey(CalendarEvent item, string groupKey) => string.Join('|',
        item.Start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        item.End?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        item.Period,
        groupKey,
        item.Type,
        item.Stage,
        item.Description,
        item.Url?.AbsoluteUri ?? "");

    private static string CanonicalGroup(
        CalendarEvent item,
        bool previous,
        IReadOnlyDictionary<string, string> aliases)
    {
        var key = GroupKey.Normalize(item.Group);
        return previous && aliases.TryGetValue(key, out var renamed) ? renamed : key;
    }

    private static bool Same(string left, string right) => Normalize(left) == Normalize(right);

    private static string Normalize(string value) => string.Join(' ', value
        .Replace('\u00a0', ' ')
        .Trim()
        .ToLowerInvariant()
        .Replace('ё', 'е')
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record GroupBucket(string Key, string Name, IReadOnlyList<CalendarEvent> Events, string? DominantUrl);
    private sealed record RenameCandidate(GroupBucket Old, GroupBucket New, double Score);
    private sealed record GroupChanges(
        IReadOnlyList<GroupChange> Added,
        IReadOnlyList<GroupChange> Removed,
        IReadOnlyList<GroupRenamed> Renamed);
}
