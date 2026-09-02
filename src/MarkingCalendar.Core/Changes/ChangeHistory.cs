namespace MarkingCalendar.Core.Changes;

public static class ChangeBatchSources
{
    public const string Local = "local";
    public const string Public = "public";
}

public sealed record ChangeBatch(
    string Id,
    DateTimeOffset CheckedAt,
    ChangeSet Changes,
    string? PreviousSnapshotId = null,
    string? CurrentSnapshotId = null,
    string Source = ChangeBatchSources.Local);

public sealed record ChangeHistory(IReadOnlyList<ChangeBatch> Batches)
{
    public static ChangeHistory Empty { get; } = new([]);
}
