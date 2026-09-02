using System.Globalization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public static class ChangeBatchIdFactory
{
    public static string FromSnapshots(string? previousSnapshotId, string currentSnapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSnapshotId);
        return EventId.FromCanonicalContent($"{previousSnapshotId ?? "initial"}|{currentSnapshotId}");
    }

    public static string FromChanges(DateTimeOffset checkedAt, ChangeSet changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var entries = changes.Added.Select(item => $"A:{item.Id}")
            .Concat(changes.Removed.Select(item => $"R:{item.Id}"))
            .Concat(changes.Moved.Select(item => $"M:{item.Previous.Id}>{item.Current.Id}"))
            .Concat(changes.Changed.Select(item => $"C:{item.Previous.Id}>{item.Current.Id}"))
            .Order(StringComparer.Ordinal);
        var timestamp = checkedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return EventId.FromCanonicalContent($"{timestamp}|{string.Join('|', entries)}");
    }
}
