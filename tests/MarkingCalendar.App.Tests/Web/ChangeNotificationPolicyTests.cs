using MarkingCalendar.App.Web;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Web;

public sealed class ChangeNotificationPolicyTests
{
    [Theory]
    [InlineData(false, false, true, true, false, true)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(false, true, true, true, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, true, true, true, false)]
    public void ShouldShow_UsesWindowStatePreferencesAndBatchHistory(
        bool windowActive,
        bool seen,
        bool enabled,
        bool hasChanges,
        bool alreadyNotified,
        bool expected)
    {
        var batch = new ChangeBatch(
            "batch-1",
            DateTimeOffset.Parse("2026-09-04T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            hasChanges
                ? new ChangeSet([], [], [], [], groupsAdded: [new GroupChange("Новая группа", 1, null)])
                : ChangeSet.Empty);
        var state = AppState.Initial.WithChangeNotifications(enabled);
        if (seen) state = state.WithSeen([batch.Id]);

        var actual = ChangeNotificationPolicy.ShouldShow(batch, state, windowActive, alreadyNotified);

        Assert.Equal(expected, actual);
    }
}
