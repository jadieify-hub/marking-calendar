namespace MarkingCalendar.Core.Snapshots;

public interface ISnapshotValidator
{
    SnapshotValidationResult Validate(CalendarSnapshot candidate, CalendarSnapshot? baseline);
}

