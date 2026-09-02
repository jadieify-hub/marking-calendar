using MarkingCalendar.Core.Changes;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class ChangeHistoryMergerTests
{
    [Fact]
    public void Merge_RemovesLocalEdgeCoveredByPublicChain()
    {
        var local = History(Batch("local", 10, "a", "c", ChangeBatchSources.Local));
        var shared = History(
            Batch("public-2", 9, "b", "c", ChangeBatchSources.Public),
            Batch("public-1", 8, "a", "b", ChangeBatchSources.Public));

        var result = ChangeHistoryMerger.Merge(local, shared);

        Assert.Equal(["public-2", "public-1"], result.Batches.Select(batch => batch.Id));
    }

    [Fact]
    public void Merge_KeepsUncoveredAndLegacyLocalBatches()
    {
        var local = History(
            Batch("uncovered", 10, "x", "y", ChangeBatchSources.Local),
            new ChangeBatch("legacy", At(7), ChangeSet.Empty));
        var shared = History(Batch("public", 8, "a", "b", ChangeBatchSources.Public));

        var result = ChangeHistoryMerger.Merge(local, shared);

        Assert.Equal(["uncovered", "public", "legacy"], result.Batches.Select(batch => batch.Id));
    }

    [Fact]
    public void Merge_PublicBatchWinsDuplicateIdAndResultIsLimited()
    {
        var localDuplicate = Batch("same", 700, "a", "b", ChangeBatchSources.Local);
        var publicDuplicate = Batch("same", 600, "a", "b", ChangeBatchSources.Public);
        var publicBatches = Enumerable.Range(1, 505)
            .Select(index => Batch($"public-{index}", index, $"p-{index}", $"c-{index}", ChangeBatchSources.Public))
            .Append(publicDuplicate)
            .ToArray();

        var result = ChangeHistoryMerger.Merge(History(localDuplicate), History(publicBatches));

        Assert.Equal(500, result.Batches.Count);
        Assert.Equal(ChangeBatchSources.Public, Assert.Single(result.Batches, batch => batch.Id == "same").Source);
        Assert.Equal(result.Batches.OrderByDescending(batch => batch.CheckedAt).Select(batch => batch.Id), result.Batches.Select(batch => batch.Id));
    }

    private static ChangeHistory History(params ChangeBatch[] batches) => new(batches);

    private static ChangeBatch Batch(string id, int minute, string previous, string current, string source) =>
        new(id, At(minute), ChangeSet.Empty, previous, current, source);

    private static DateTimeOffset At(int minute) => new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute);
}
