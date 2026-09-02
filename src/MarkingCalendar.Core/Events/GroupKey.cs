namespace MarkingCalendar.Core.Events;

public static class GroupKey
{
    public static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty)
        .Replace('\u00a0', ' ')
        .Trim()
        .ToLowerInvariant()
        .Replace('ё', 'е')
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
