using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public sealed record GroupNovelty(DateTimeOffset? FirstSeen, string? RenamedFrom, DateTimeOffset? RenamedAt);

public static class GroupNoveltyBuilder
{
    public static IReadOnlyDictionary<string, GroupNovelty> Build(ChangeHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var result = new Dictionary<string, GroupNovelty>(StringComparer.Ordinal);
        foreach (var batch in history.Batches.OrderBy(item => item.CheckedAt).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (var group in batch.Changes.GroupsAdded)
            {
                var key = GroupKey.Normalize(group.Name);
                if (!result.ContainsKey(key)) result[key] = new GroupNovelty(batch.CheckedAt, null, null);
            }
            foreach (var rename in batch.Changes.GroupsRenamed)
            {
                var oldKey = GroupKey.Normalize(rename.From);
                var newKey = GroupKey.Normalize(rename.To);
                result.TryGetValue(oldKey, out var previous);
                result.Remove(oldKey);
                result[newKey] = new GroupNovelty(previous?.FirstSeen, rename.From, batch.CheckedAt);
            }
        }
        return result;
    }
}
