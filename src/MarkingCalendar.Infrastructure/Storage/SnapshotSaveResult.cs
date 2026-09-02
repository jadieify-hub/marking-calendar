using MarkingCalendar.Core.Snapshots;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed record SnapshotSaveResult(bool Saved, SnapshotValidationResult Validation)
{
    public static SnapshotSaveResult Rejected(SnapshotValidationResult validation) => new(false, validation);
    public static SnapshotSaveResult Success { get; } = new(true, SnapshotValidationResult.Valid);
}

