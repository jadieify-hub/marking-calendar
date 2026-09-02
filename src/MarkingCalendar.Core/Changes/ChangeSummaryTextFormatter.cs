using System.Globalization;

namespace MarkingCalendar.Core.Changes;

public static class ChangeSummaryTextFormatter
{
    private const int EventLimit = 30;

    public static string Format(
        ChangeSummaryResult summary,
        DateTimeOffset checkedAt,
        IReadOnlySet<string> selectedGroups)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(selectedGroups);
        var lines = new List<string>
        {
            $"Календарь маркировки — изменения от {checkedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"))}",
            $"Перенесено {summary.Counts.Moved}, добавлено {summary.Counts.Added}, изменено {summary.Counts.Changed}, удалено {summary.Counts.Removed}"
        };
        var emitted = 0;
        if (selectedGroups.Count == 0)
        {
            lines.Add($"Изменения ({summary.Counts.Total}):");
            emitted += AppendItems(lines, summary.Items, EventLimit);
        }
        else
        {
            lines.Add($"По вашим группам ({summary.MineCount}):");
            emitted += AppendItems(lines, summary.Items.Where(item => item.Mine), EventLimit);
            lines.Add($"По остальным группам ({summary.OthersCount}):");
            emitted += AppendItems(lines, summary.Items.Where(item => !item.Mine), EventLimit - emitted);
        }

        var omitted = Math.Max(0, summary.Counts.Total - emitted);
        if (omitted > 0) lines.Add($"и ещё {omitted}");
        lines.Add("Источник: честныйзнак.рф, проверено приложением «Календарь маркировки»");
        return string.Join('\n', lines);
    }

    private static int AppendItems(List<string> lines, IEnumerable<ChangeSummary> items, int limit)
    {
        var count = 0;
        foreach (var item in items.Take(Math.Max(0, limit)))
        {
            lines.Add($"• {item.Title}: {item.Detail.TrimEnd().TrimEnd('.')}.");
            if (!string.IsNullOrWhiteSpace(item.Stage)) lines.Add($"  {item.Stage.Trim()}");
            count++;
        }
        return count;
    }
}
