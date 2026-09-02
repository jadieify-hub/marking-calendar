using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Source;

public sealed class PublicHistorySyncPolicyTests
{
    [Fact]
    public void ShouldSync_RequiresEnabledSettingAndOneDayInterval()
    {
        var now = new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);

        Assert.True(PublicHistorySyncPolicy.ShouldSync(AppState.Initial, now));
        Assert.False(PublicHistorySyncPolicy.ShouldSync(AppState.Initial.WithPublicHistory(false), now));
        Assert.False(PublicHistorySyncPolicy.ShouldSync(AppState.Initial.WithPublicHistorySync(now.AddHours(-23), []), now));
        Assert.True(PublicHistorySyncPolicy.ShouldSync(AppState.Initial.WithPublicHistorySync(now.AddDays(-1), []), now));
    }

    [Fact]
    public void Apply_FirstSyncMarksEveryPublicBatchSeen()
    {
        var now = new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
        var history = History(Batch("new", now), Batch("old", now.AddDays(-10)));

        var state = PublicHistorySyncPolicy.Apply(AppState.Initial, history, now.AddDays(-2), now);

        Assert.Equal(["new", "old"], state.SeenBatchIds);
        Assert.Equal(now, state.LastPublicHistorySync);
    }

    [Fact]
    public void Apply_SubsequentSyncMarksOnlyBatchesOlderThanLocalSnapshotSeen()
    {
        var now = new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
        var previous = AppState.Initial.WithPublicHistorySync(now.AddDays(-2), ["already"]);
        var localRetrievedAt = now.AddDays(-1);
        var history = History(Batch("new", now), Batch("old", now.AddDays(-2)));

        var state = PublicHistorySyncPolicy.Apply(previous, history, localRetrievedAt, now);

        Assert.Equal(["already", "old"], state.SeenBatchIds);
        Assert.DoesNotContain("new", state.SeenBatchIds);
    }

    private static ChangeHistory History(params ChangeBatch[] batches) => new(batches);
    private static ChangeBatch Batch(string id, DateTimeOffset checkedAt) =>
        new(id, checkedAt, ChangeSet.Empty, "a", "b", ChangeBatchSources.Public);
}
