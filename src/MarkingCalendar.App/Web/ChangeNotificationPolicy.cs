using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Web;

public static class ChangeNotificationPolicy
{
    public static bool ShouldShow(ChangeBatch batch, AppState state, bool windowActive, bool alreadyNotified)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(state);
        return state.ChangeNotificationsEnabled
            && batch.Changes.HasChanges
            && !windowActive
            && !alreadyNotified
            && !state.SeenBatchIds.Contains(batch.Id, StringComparer.Ordinal);
    }
}
