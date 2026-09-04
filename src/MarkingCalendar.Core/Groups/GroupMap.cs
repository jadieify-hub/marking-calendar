using System.Globalization;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Groups;

public static class GroupMapStatuses
{
    public const string Completed = "completed";
}

public sealed record GroupSector(string Id, string Label);

public sealed record GroupMapEntry(
    string Name,
    string Link,
    IReadOnlyList<string> Sectors,
    string? Status = null,
    bool? GoodsPath = null)
{
    public bool IsCompleted => Status == GroupMapStatuses.Completed;
}

public sealed record GroupMap(
    int SchemaVersion,
    string UpdatedAt,
    IReadOnlyList<GroupSector> Sectors,
    IReadOnlyList<GroupMapEntry> Groups);

public sealed class GroupMapValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(' ', errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class GroupMapValidator
{
    public static IReadOnlyList<string> Validate(GroupMap? map)
    {
        if (map is null) return ["Карта групп содержит пустой JSON."];
        var errors = new List<string>();
        if (map.SchemaVersion != 2) errors.Add($"Версия схемы карты групп не поддерживается: {map.SchemaVersion}.");
        if (!DateOnly.TryParseExact(map.UpdatedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("Поле updatedAt должно содержать дату в формате yyyy-MM-dd.");
        }

        var sectors = map.Sectors ?? [];
        var groups = map.Groups ?? [];
        var sectorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sector in sectors)
        {
            if (string.IsNullOrWhiteSpace(sector.Id) || string.IsNullOrWhiteSpace(sector.Label))
            {
                errors.Add("У каждой отрасли должны быть непустые id и label.");
                continue;
            }
            if (!sectorIds.Add(sector.Id)) errors.Add($"Идентификатор отрасли повторяется: {sector.Id}.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var key = GroupKey.Normalize(group.Name);
            if (key.Length == 0) errors.Add("Имя группы не может быть пустым.");
            else if (!names.Add(key)) errors.Add($"Нормализованное имя группы повторяется: {group.Name}.");

            var link = GroupMapMatcher.NormalizeLink(group.Link);
            if (link.Length == 0) errors.Add($"У группы «{group.Name}» не указана ссылка.");
            else if (!links.Add(link)) errors.Add($"Ссылка группы повторяется: {group.Link}.");

            if (group.Sectors is null || group.Sectors.Count == 0)
            {
                errors.Add($"У группы «{group.Name}» не указана отрасль.");
            }
            else
            {
                foreach (var sectorId in group.Sectors)
                {
                    if (!sectorIds.Contains(sectorId)) errors.Add($"Группа «{group.Name}» ссылается на неизвестную отрасль {sectorId}.");
                }
            }

            if (group.Status is not null && group.Status != GroupMapStatuses.Completed)
            {
                errors.Add($"У группы «{group.Name}» указан неизвестный статус {group.Status}.");
            }
        }
        return errors;
    }

    public static void EnsureValid(GroupMap? map)
    {
        var errors = Validate(map);
        if (errors.Count > 0) throw new GroupMapValidationException(errors);
    }
}

public sealed record GroupMapMatch(string SnapshotGroup, string? Link, GroupMapEntry Entry, bool NameConflict);

public sealed record GroupMapMatchReport(
    IReadOnlyList<GroupMapMatch> Matches,
    IReadOnlyList<string> SnapshotOnly,
    IReadOnlyList<string> MapOnly);

public static class GroupMapMatcher
{
    public static GroupMapMatchReport Match(GroupMap map, IReadOnlyList<CalendarEvent> events)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(events);
        GroupMapValidator.EnsureValid(map);
        var byLink = map.Groups.ToDictionary(item => NormalizeLink(item.Link), StringComparer.OrdinalIgnoreCase);
        var byName = map.Groups.ToDictionary(item => GroupKey.Normalize(item.Name), StringComparer.Ordinal);
        var matchedLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<GroupMapMatch>();
        var snapshotOnly = new List<string>();

        foreach (var group in events.GroupBy(item => GroupKey.Normalize(item.Group), StringComparer.Ordinal))
        {
            var items = group.ToArray();
            var name = items[0].Group.Replace('\u00a0', ' ').Trim();
            var link = items
                .Select(item => item.Url is null ? null : NormalizeLink(item.Url.OriginalString))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .GroupBy(item => item!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Key)
                .FirstOrDefault();
            GroupMapEntry? entry = null;
            var matchedByLink = link is not null && byLink.TryGetValue(link, out entry);
            if (!matchedByLink) byName.TryGetValue(group.Key, out entry);
            if (entry is null)
            {
                snapshotOnly.Add(name);
                continue;
            }

            matchedLinks.Add(NormalizeLink(entry.Link));
            matches.Add(new GroupMapMatch(
                name,
                link,
                entry,
                matchedByLink && GroupKey.Normalize(name) != GroupKey.Normalize(entry.Name)));
        }

        var mapOnly = map.Groups
            .Where(entry => !matchedLinks.Contains(NormalizeLink(entry.Link)))
            .Select(entry => entry.Name)
            .ToArray();
        return new GroupMapMatchReport(matches, snapshotOnly, mapOnly);
    }

    internal static string NormalizeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : trimmed;
    }
}
