using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Groups;

public static class GroupSelectionCalculator
{
    public static IReadOnlyList<string> Calculate(
        GroupMap map,
        IEnumerable<string> selectedSectors,
        IReadOnlyDictionary<string, bool> manualGroups)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(selectedSectors);
        ArgumentNullException.ThrowIfNull(manualGroups);
        var sectors = selectedSectors.ToHashSet(StringComparer.Ordinal);
        var selected = map.Groups
            .Where(group => !group.IsCompleted && group.Sectors.Any(sectors.Contains))
            .Select(group => GroupKey.Normalize(group.Name))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (rawKey, included) in manualGroups)
        {
            var key = GroupKey.Normalize(rawKey);
            if (key.Length == 0) continue;
            if (included) selected.Add(key);
            else selected.Remove(key);
        }
        return selected.OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyDictionary<string, bool> CaptureOverrides(
        GroupMap map,
        IEnumerable<string> selectedSectors,
        IEnumerable<string> desiredGroups)
    {
        ArgumentNullException.ThrowIfNull(desiredGroups);
        var defaults = Calculate(map, selectedSectors, new Dictionary<string, bool>()).ToHashSet(StringComparer.Ordinal);
        var desired = desiredGroups.Select(GroupKey.Normalize).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        return map.Groups
            .Select(group => GroupKey.Normalize(group.Name))
            .Concat(desired)
            .Distinct(StringComparer.Ordinal)
            .Where(key => defaults.Contains(key) != desired.Contains(key))
            .ToDictionary(key => key, desired.Contains, StringComparer.Ordinal);
    }
}
