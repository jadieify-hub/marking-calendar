using System.Globalization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Changes;

public static class ChangeMarkdownFormatter
{
    private const int BatchLimit = 200;
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static string Format(ChangeHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var batches = history.Batches
            .OrderByDescending(batch => batch.CheckedAt)
            .Take(BatchLimit)
            .ToArray();
        var lines = new List<string>
        {
            "# История изменений календаря маркировки",
            string.Empty,
            "Обновляется автоматически, источник честныйзнак.рф, приложение не является официальным продуктом оператора."
        };

        foreach (var batch in batches)
        {
            var checkedAt = batch.CheckedAt.ToOffset(MoscowOffset);
            lines.Add(string.Empty);
            lines.Add($"## {checkedAt.ToString("dd.MM.yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"))} МСК — {CountText(batch.Changes.Total)}");
            AppendSection(lines, "Перенесено", batch.Changes.Moved, AppendMoved);
            AppendSection(lines, "Добавлено", batch.Changes.Added, AppendEvent);
            AppendSection(lines, "Изменено", batch.Changes.Changed, AppendChanged);
            AppendSection(lines, "Удалено", batch.Changes.Removed, AppendEvent);
        }

        if (history.Batches.Count > BatchLimit)
        {
            lines.Add(string.Empty);
            lines.Add("Более ранние записи доступны в [history/changes.json](history/changes.json).");
        }

        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }

    internal static string BatchTitle(ChangeBatch batch)
    {
        var checkedAt = batch.CheckedAt.ToOffset(MoscowOffset);
        return $"{checkedAt.ToString("dd.MM.yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"))} МСК — {CountText(batch.Changes.Total)}";
    }

    private static void AppendSection<T>(List<string> lines, string title, IReadOnlyList<T> items, Action<List<string>, T> append)
    {
        if (items.Count == 0) return;
        lines.Add(string.Empty);
        lines.Add($"**{title} ({items.Count.ToString(CultureInfo.InvariantCulture)})**");
        lines.Add(string.Empty);
        foreach (var item in items) append(lines, item);
    }

    private static void AppendMoved(List<string> lines, EventChange change)
    {
        lines.Add($"- {EventPrefix(change.Current)}: {EventPeriodChangeFormatter.Format(change)}. {Escape(change.Current.Stage)}");
        AppendChangedFields(lines, change.GetChangedFields());
    }

    private static void AppendChanged(List<string> lines, EventChange change)
    {
        lines.Add($"- {EventPrefix(change.Current)}: {Date(change.Current)}. {Escape(change.Current.Stage)}");
        AppendChangedFields(lines, change.GetChangedFields());
    }

    private static void AppendEvent(List<string> lines, CalendarEvent item) =>
        lines.Add($"- {EventPrefix(item)}: {Date(item)}. {Escape(item.Stage)}");

    private static void AppendChangedFields(List<string> lines, IReadOnlyList<ChangedField> fields)
    {
        foreach (var field in fields)
        {
            var label = FieldLabel(field.Field);
            lines.Add($"  - {label} — было: {Escape(field.Previous)}");
            lines.Add($"  - {label} — стало: {Escape(field.Current)}");
        }
    }

    private static string EventPrefix(CalendarEvent item) => $"{Escape(item.Group)} — {Escape(item.Type)}";

    private static string Date(CalendarEvent item) =>
        (item.Start ?? item.End)?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "дата не указана";

    private static string CountText(int count)
    {
        var suffix = count % 10 == 1 && count % 100 != 11
            ? "изменение"
            : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14
                ? "изменения"
                : "изменений";
        return $"{count.ToString(CultureInfo.InvariantCulture)} {suffix}";
    }

    private static string FieldLabel(string field) => field switch
    {
        "stage" => "этап",
        "description" => "описание",
        "period" => "период",
        "url" => "ссылка",
        _ => "поле"
    };

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
