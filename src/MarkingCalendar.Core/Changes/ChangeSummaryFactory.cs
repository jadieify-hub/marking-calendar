using System.Globalization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public sealed record ChangeCounts(int Added, int Removed, int Moved, int Changed)
{
    public int Total => Added + Removed + Moved + Changed;
}

public sealed record ChangeSummary(
    ChangeKind Kind,
    string Title,
    string Detail,
    string Stage,
    DateOnly? Date,
    IReadOnlyList<ChangedField> ChangedFields,
    bool Mine,
    EventCategory Category = EventCategory.Other);

public sealed record ChangeSummaryResult(
    ChangeCounts Counts,
    IReadOnlyList<ChangeSummary> Items,
    int MineCount,
    int OthersCount);

public sealed class ChangeSummaryFactory : IChangeSummaryFactory
{
    public ChangeSummaryResult Create(
        ChangeSet changes,
        int limit,
        DateOnly today,
        IReadOnlySet<string> selectedGroups,
        IReadOnlySet<EventCategory>? priorityCategories = null)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(selectedGroups);
        priorityCategories ??= new HashSet<EventCategory>();
        var mine = selectedGroups.Select(GroupKey.Normalize).ToHashSet(StringComparer.Ordinal);
        var items = new List<ChangeSummary>();
        items.AddRange(changes.Moved.Select(change => MoveSummary(change, mine)));
        items.AddRange(changes.Added.Select(item => EventSummary(ChangeKind.Added, item, FormatDate(EventDate(item)), [], mine)));
        items.AddRange(changes.Changed.Select(item => EventSummary(ChangeKind.Changed, item.Current, $"{FormatDate(EventDate(item.Current))} · изменены параметры", item.GetChangedFields(), mine)));
        items.AddRange(changes.Removed.Select(item => EventSummary(ChangeKind.Removed, item, FormatDate(EventDate(item)), [], mine)));

        var selected = items
            .OrderBy(item => mine.Count > 0 && !item.Mine ? 1 : 0)
            .ThenBy(item => SortBucket(item.Date, today))
            .ThenBy(item => SortDistance(item.Date, today))
            .ThenBy(item => priorityCategories.Contains(item.Category) ? 0 : 1)
            .ThenBy(item => KindPriority(item.Kind))
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .Take(Math.Max(0, limit))
            .ToArray();
        var counts = new ChangeCounts(changes.Added.Count, changes.Removed.Count, changes.Moved.Count, changes.Changed.Count);
        var mineCount = mine.Count == 0 ? 0 : items.Count(item => item.Mine);
        return new ChangeSummaryResult(counts, selected, mineCount, counts.Total - mineCount);
    }

    private static ChangeSummary MoveSummary(EventChange change, IReadOnlySet<string> selectedGroups)
    {
        var detail = EventPeriodChangeFormatter.Format(change);
        if (change.WordingChanged)
        {
            detail += ", формулировка изменена";
        }

        return EventSummary(ChangeKind.Moved, change.Current, detail, change.WordingChanged ? change.GetChangedFields() : [], selectedGroups);
    }

    private static ChangeSummary EventSummary(
        ChangeKind kind,
        CalendarEvent item,
        string detail,
        IReadOnlyList<ChangedField> changedFields,
        IReadOnlySet<string> selectedGroups) =>
        new(
            kind,
            $"{item.Group} — {item.Type}",
            detail,
            item.Stage,
            EventDate(item),
            changedFields,
            selectedGroups.Count > 0 && selectedGroups.Contains(GroupKey.Normalize(item.Group)),
            EventClassifier.Classify(item.Type, item.Stage));

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "дата не указана";

    private static DateOnly? EventDate(CalendarEvent item) => item.Start ?? item.End;

    private static int SortBucket(DateOnly? date, DateOnly today) => date is not null && date >= today ? 0 : 1;

    private static int SortDistance(DateOnly? date, DateOnly today) =>
        date is null ? int.MaxValue : Math.Abs(date.Value.DayNumber - today.DayNumber);

    private static int KindPriority(ChangeKind kind) => kind switch
    {
        ChangeKind.Moved => 0,
        ChangeKind.Changed => 1,
        ChangeKind.Added => 2,
        ChangeKind.Removed => 3,
        _ => 4
    };
}
