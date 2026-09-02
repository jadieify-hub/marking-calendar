namespace MarkingCalendar.Core.Events;

public static class UserRoleCategories
{
    public static IReadOnlySet<EventCategory> For(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var categories = new HashSet<EventCategory>();
        foreach (var role in roles)
        {
            categories.UnionWith(role switch
            {
                "retail" =>
                [
                    EventCategory.Retail,
                    EventCategory.Permit,
                    EventCategory.Ban,
                    EventCategory.Edo,
                    EventCategory.Registration
                ],
                "producer" =>
                [
                    EventCategory.Marking,
                    EventCategory.Registration,
                    EventCategory.Edo,
                    EventCategory.Ban
                ],
                "wholesale" =>
                [
                    EventCategory.Edo,
                    EventCategory.Ban,
                    EventCategory.Registration
                ],
                _ => []
            });
        }

        return categories;
    }
}
