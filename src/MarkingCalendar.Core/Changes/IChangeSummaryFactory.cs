using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public interface IChangeSummaryFactory
{
    ChangeSummaryResult Create(
        ChangeSet changes,
        int limit,
        DateOnly today,
        IReadOnlySet<string> selectedGroups,
        IReadOnlySet<EventCategory>? priorityCategories = null);
}
