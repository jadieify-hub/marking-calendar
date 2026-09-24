using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed record AppliedGroupRename(string BatchId, GroupRenamed Rename);
public sealed record GroupSubscriptionUpdateResult(AppState State, IReadOnlyList<AppliedGroupRename> AppliedRenames);

public static class GroupSubscriptionUpdater
{
    public static GroupSubscriptionUpdateResult Apply(AppState state, IEnumerable<ChangeBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(batches);
        state = AppState.Normalize(state);
        var selected = state.SelectedGroups.ToHashSet(StringComparer.Ordinal);
        var manual = new Dictionary<string, bool>(state.ManualGroups, StringComparer.Ordinal);
        var applied = new List<AppliedGroupRename>();
        foreach (var batch in batches.OrderBy(item => item.CheckedAt).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            foreach (var rename in batch.Changes.GroupsRenamed)
            {
                var from = GroupKey.Normalize(rename.From);
                var to = GroupKey.Normalize(rename.To);
                var changed = false;
                if (selected.Remove(from))
                {
                    selected.Add(to);
                    changed = true;
                }

                if (manual.Remove(from, out var included))
                {
                    manual.TryAdd(to, included);
                    changed = true;
                }
                if (changed) applied.Add(new AppliedGroupRename(batch.Id, rename));
            }
        }

        return applied.Count == 0
            ? new GroupSubscriptionUpdateResult(state, [])
            : new GroupSubscriptionUpdateResult(state.WithGroupPreferences(selected, manual), applied);
    }
}
