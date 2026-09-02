namespace MarkingCalendar.Core.Changes;

public static class ChangeHistoryMerger
{
    private const int BatchLimit = 500;

    public static ChangeHistory Merge(ChangeHistory local, ChangeHistory shared)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(shared);
        var publicById = shared.Batches
            .GroupBy(batch => batch.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var graph = BuildGraph(publicById.Values);
        var retainedLocal = local.Batches.Where(batch =>
            !publicById.ContainsKey(batch.Id)
            && !CoveredByPublicPath(batch, graph));
        var merged = publicById.Values
            .Concat(retainedLocal)
            .OrderByDescending(batch => batch.CheckedAt)
            .ThenBy(batch => batch.Id, StringComparer.Ordinal)
            .Take(BatchLimit)
            .ToArray();
        return new ChangeHistory(merged);
    }

    private static Dictionary<string, HashSet<string>> BuildGraph(IEnumerable<ChangeBatch> batches)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch.PreviousSnapshotId) || string.IsNullOrWhiteSpace(batch.CurrentSnapshotId)) continue;
            if (!graph.TryGetValue(batch.PreviousSnapshotId, out var targets))
            {
                targets = new HashSet<string>(StringComparer.Ordinal);
                graph.Add(batch.PreviousSnapshotId, targets);
            }

            targets.Add(batch.CurrentSnapshotId);
        }

        return graph;
    }

    private static bool CoveredByPublicPath(ChangeBatch batch, Dictionary<string, HashSet<string>> graph)
    {
        if (string.IsNullOrWhiteSpace(batch.PreviousSnapshotId) || string.IsNullOrWhiteSpace(batch.CurrentSnapshotId)) return false;
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { batch.PreviousSnapshotId };
        pending.Enqueue(batch.PreviousSnapshotId);
        while (pending.TryDequeue(out var current))
        {
            if (!graph.TryGetValue(current, out var targets)) continue;
            foreach (var target in targets)
            {
                if (target.Equals(batch.CurrentSnapshotId, StringComparison.Ordinal)) return true;
                if (visited.Add(target)) pending.Enqueue(target);
            }
        }

        return false;
    }
}
