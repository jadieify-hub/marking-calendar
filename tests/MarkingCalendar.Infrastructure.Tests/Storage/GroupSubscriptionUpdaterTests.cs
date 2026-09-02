using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class GroupSubscriptionUpdaterTests
{
    [Fact]
    public void Apply_TransfersSubscriptionAcrossRenameChain()
    {
        var state = AppState.Initial.WithGroups([" Медицинские изделия "]);
        var history = new[]
        {
            Batch("second", new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero), "Медицинские изделия 2.0", "Медицинские изделия и приборы"),
            Batch("first", new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), "Медицинские изделия", "Медицинские изделия 2.0")
        };

        var result = GroupSubscriptionUpdater.Apply(state, history);

        Assert.Equal(["медицинские изделия и приборы"], result.State.SelectedGroups);
        Assert.Collection(
            result.AppliedRenames,
            item => Assert.Equal("Медицинские изделия 2.0", item.Rename.To),
            item => Assert.Equal("Медицинские изделия и приборы", item.Rename.To));
    }

    [Fact]
    public void Apply_TransfersManualIncludeAndExcludeAcrossRenames()
    {
        var state = AppState.Initial.WithGroupPreferences(
            ["Старая включённая"],
            new Dictionary<string, bool>
            {
                ["старая включённая"] = true,
                ["старая исключённая"] = false
            });
        var batch = new ChangeBatch(
            "rename",
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            new ChangeSet([], [], [], [], groupsRenamed:
            [
                new GroupRenamed("Старая включённая", "Новая включённая"),
                new GroupRenamed("Старая исключённая", "Новая исключённая")
            ]));

        var result = GroupSubscriptionUpdater.Apply(state, [batch]);

        Assert.Equal(["новая включенная"], result.State.SelectedGroups);
        Assert.True(result.State.ManualGroups["новая включенная"]);
        Assert.False(result.State.ManualGroups["новая исключенная"]);
        Assert.DoesNotContain("старая включенная", result.State.ManualGroups.Keys);
        Assert.DoesNotContain("старая исключенная", result.State.ManualGroups.Keys);
    }

    private static ChangeBatch Batch(string id, DateTimeOffset checkedAt, string from, string to) => new(
        id,
        checkedAt,
        new ChangeSet([], [], [], [], groupsRenamed: [new GroupRenamed(from, to)]));
}
