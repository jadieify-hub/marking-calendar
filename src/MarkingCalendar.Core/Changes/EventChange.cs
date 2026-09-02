using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public sealed record ChangedField(string Field, string Previous, string Current);

public sealed record EventChange(ChangeKind Kind, CalendarEvent Previous, CalendarEvent Current, bool WordingChanged)
{
    public static EventChange Moved(CalendarEvent previous, CalendarEvent current) =>
        new(ChangeKind.Moved, previous, current, HasWordingChanges(previous, current));

    public static EventChange Changed(CalendarEvent previous, CalendarEvent current) =>
        new(ChangeKind.Changed, previous, current, HasWordingChanges(previous, current));

    public IReadOnlyList<ChangedField> GetChangedFields() => BuildChangedFields(Previous, Current);

    private static bool HasWordingChanges(CalendarEvent previous, CalendarEvent current) =>
        BuildChangedFields(previous, current).Count > 0;

    private static List<ChangedField> BuildChangedFields(CalendarEvent previous, CalendarEvent current)
    {
        var fields = new List<ChangedField>(4);
        AddIfChanged(fields, "stage", previous.Stage, current.Stage);
        AddIfChanged(fields, "description", previous.Description, current.Description);
        AddIfChanged(fields, "period", previous.Period, current.Period);
        AddIfChanged(fields, "url", previous.Url?.AbsoluteUri ?? string.Empty, current.Url?.AbsoluteUri ?? string.Empty);
        return fields;
    }

    private static void AddIfChanged(List<ChangedField> fields, string field, string previous, string current)
    {
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            fields.Add(new ChangedField(field, previous, current));
        }
    }
}
