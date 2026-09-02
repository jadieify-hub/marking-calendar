using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Snapshots;

namespace MarkingCalendar.Infrastructure.Updates;

public enum CalendarUpdateStatus
{
    NoChanges,
    Updated,
    Rejected,
    Failed
}

public sealed record CalendarUpdateResult(
    CalendarUpdateStatus Status,
    CalendarSnapshot? Snapshot,
    ChangeSet Changes,
    ChangeBatch? Batch,
    string UserMessage,
    Exception? DiagnosticError = null);

