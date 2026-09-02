namespace MarkingCalendar.Core.Snapshots;

public enum SnapshotValidationErrorCode
{
    Empty,
    RequiredField,
    DuplicateId,
    InvalidDateRange,
    AnomalousCount
}

public sealed record SnapshotValidationError(SnapshotValidationErrorCode Code, string Message);

public sealed record SnapshotValidationResult(IReadOnlyList<SnapshotValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static SnapshotValidationResult Valid { get; } = new([]);
}

public sealed record SnapshotValidationOptions(
    int MinimumExpectedEvents = 100,
    double MinimumBaselineRatio = 0.5,
    int EarliestYear = 2010,
    int LatestYear = 2100);

