using MarkingCalendar.Core.Changes;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class GroupNoveltyBuilderTests
{
    [Fact]
    public void Build_TracksFirstAppearanceAndLatestRename()
    {
        var firstSeen = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var renamedAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var history = new ChangeHistory([
            Batch("rename", renamedAt, new ChangeSet([], [], [], [], groupsRenamed: [new GroupRenamed("Медизделия", "Медицинские изделия 2.0")])),
            Batch("added", firstSeen, new ChangeSet([], [], [], [], groupsAdded: [new GroupChange("Медизделия", 2, new DateOnly(2027, 1, 1))]))
        ]);

        var novelty = GroupNoveltyBuilder.Build(history)["медицинские изделия 2.0"];

        Assert.Equal(firstSeen, novelty.FirstSeen);
        Assert.Equal("Медизделия", novelty.RenamedFrom);
        Assert.Equal(renamedAt, novelty.RenamedAt);
    }

    private static ChangeBatch Batch(string id, DateTimeOffset checkedAt, ChangeSet changes) => new(id, checkedAt, changes);
}
