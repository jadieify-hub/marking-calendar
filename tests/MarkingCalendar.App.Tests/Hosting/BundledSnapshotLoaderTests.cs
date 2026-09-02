using System.Text;
using System.Text.Json;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class BundledSnapshotLoaderTests
{
    [Fact]
    public async Task LoadGroupsAsync_ReadsAndValidatesEmbeddedShape()
    {
        await using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            {"schemaVersion":2,"updatedAt":"2026-09-02","sectors":[{"id":"food","label":"Продукты"}],"groups":[{"name":"Бакалея","link":"/business/projects/grocery/","sectors":["food"]}]}
            """));

        var map = await BundledSnapshotLoader.LoadGroupsAsync(source, CancellationToken.None);

        Assert.Equal("food", Assert.Single(map.Sectors).Id);
        Assert.Equal("Бакалея", Assert.Single(map.Groups).Name);
    }

    [Fact]
    public async Task LoadHistoryAsync_ReadsTheEmbeddedPublicHistoryResource()
    {
        await using var stream = typeof(AppBootstrapper).Assembly
            .GetManifestResourceStream("MarkingCalendar.Resources.bundled-history.json")
            ?? throw new InvalidOperationException("Встроенная история не найдена.");
        var history = await BundledSnapshotLoader.LoadHistoryAsync(stream, CancellationToken.None);

        Assert.NotNull(history.Batches);
        Assert.All(history.Batches, batch => Assert.Equal(ChangeBatchSources.Public, batch.Source));
    }

    [Fact]
    public async Task LoadHistoryAsync_NormalizesEveryBundledBatchAsPublic()
    {
        var serialized = JsonSerializer.Serialize(new ChangeHistory([
            new ChangeBatch("bundled", new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), ChangeSet.Empty)
        ]), JsonDefaults.Options);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serialized));
        var history = await BundledSnapshotLoader.LoadHistoryAsync(stream, CancellationToken.None);

        Assert.Equal(ChangeBatchSources.Public, Assert.Single(history.Batches).Source);
    }

    [Fact]
    public async Task LoadAsync_UsesTheSameNormalizationAsNetworkPayloads()
    {
        const string json = """
            {"data":{"items":[{
              "date_start":"01.09.2026","date_end":null,"date_period_text":"с 1 сентября",
              "tg_name":"Игрушки","event":"Розничная продажа","stage":"Старт",
              "description":"Описание","tg_link":"/business/projects/children/"
            }]}}
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var metadata = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "retrievedAt": "2026-09-01T10:45:00+03:00",
              "sourceUrl": "https://честныйзнак.рф/source",
              "itemCount": 1
            }
            """));
        var loader = new BundledSnapshotLoader(new EventNormalizer());

        var snapshot = await loader.LoadAsync(
            stream,
            metadata,
            CancellationToken.None);

        var item = Assert.Single(snapshot.Events);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 45, 0, TimeSpan.FromHours(3)), snapshot.RetrievedAt);
        Assert.Equal("https://честныйзнак.рф/source", snapshot.SourceUrl.AbsoluteUri);
        Assert.Equal(new DateOnly(2026, 9, 1), item.Start);
        Assert.Equal("https://честныйзнак.рф/business/projects/children/", item.Url?.AbsoluteUri);
    }

    [Fact]
    public async Task LoadAsync_RejectsMetadataThatDoesNotMatchThePayload()
    {
        const string json = """{"data":{"items":[{"date_start":"01.09.2026","tg_name":"Игрушки","event":"Розничная продажа","stage":"Старт"}]}}""";
        const string metadataJson = """{"retrievedAt":"2026-09-01T10:45:00+03:00","sourceUrl":"https://честныйзнак.рф/source","itemCount":2}""";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await using var metadata = new MemoryStream(Encoding.UTF8.GetBytes(metadataJson));
        var loader = new BundledSnapshotLoader(new EventNormalizer());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync(stream, metadata, CancellationToken.None));

        Assert.Contains("число событий", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
