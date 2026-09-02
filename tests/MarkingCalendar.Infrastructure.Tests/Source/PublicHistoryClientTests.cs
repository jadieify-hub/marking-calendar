using System.Net;
using System.Text;
using System.Text.Json;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Source;

public sealed class PublicHistoryClientTests
{
    private static readonly Uri ManifestUrl = new("https://raw.githubusercontent.com/jadieify-hub/marking-calendar/data/manifest.json");

    [Fact]
    public void Constructor_RejectsUrlOutsideOfficialDataPath()
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => throw new InvalidOperationException()));

        Assert.Throws<ArgumentException>(() => new PublicHistoryClient(http, new Uri("https://example.test/manifest.json")));
        Assert.Throws<ArgumentException>(() => new PublicHistoryClient(http, new Uri("https://raw.githubusercontent.com/other/project/data/manifest.json")));
    }

    [Fact]
    public async Task FetchAsync_RejectsHistoryLargerThanTenMegabytes()
    {
        using var http = new HttpClient(new DelegateHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal)
                ? JsonResponse(Manifest())
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([])
                    {
                        Headers = { ContentLength = 10 * 1024 * 1024 + 1 }
                    }
                })));
        var client = new PublicHistoryClient(http, ManifestUrl);

        var error = await Assert.ThrowsAsync<PublicHistoryException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Contains("размер", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_RejectsUnknownManifestSchema()
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(JsonResponse(Manifest(schemaVersion: 2)))));
        var client = new PublicHistoryClient(http, ManifestUrl);

        var error = await Assert.ThrowsAsync<PublicHistoryException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Contains("схем", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_RejectsHistoryUrlOutsideOfficialDataPathAsPublicHistoryError()
    {
        var manifest = Manifest() with
        {
            Files = new PublicHistoryFiles(History: "https://example.test/history.json")
        };
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(JsonResponse(manifest))));
        var client = new PublicHistoryClient(http, ManifestUrl);

        var error = await Assert.ThrowsAsync<PublicHistoryException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Contains("официальн", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_RejectsGroupsUrlOutsideOfficialDataPathAsPublicHistoryError()
    {
        var manifest = Manifest() with { GroupsUrl = "https://example.test/groups.json" };
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(JsonResponse(manifest))));
        var client = new PublicHistoryClient(http, ManifestUrl);

        var error = await Assert.ThrowsAsync<PublicHistoryException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Contains("официальн", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_LoadsBoundedHistoryFromManifest()
    {
        var history = new ChangeHistory([
            new ChangeBatch("public", new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero), ChangeSet.Empty, "a", "b", ChangeBatchSources.Public)
        ]);
        using var http = new HttpClient(new DelegateHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("manifest.json", StringComparison.Ordinal)
                ? JsonResponse(Manifest())
                : request.RequestUri.AbsolutePath.EndsWith("groups.json", StringComparison.Ordinal)
                    ? JsonResponse(Map())
                    : JsonResponse(history))));
        var client = new PublicHistoryClient(http, ManifestUrl);

        var result = await client.FetchAsync(CancellationToken.None);

        Assert.Equal("snapshot", result.SnapshotId);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero), result.GeneratedAt);
        Assert.Equal("public", Assert.Single(result.History.Batches).Id);
        Assert.Equal("food", Assert.Single(result.Groups.Sectors).Id);
    }

    private static PublicHistoryManifest Manifest(int schemaVersion = 1) => new(
        schemaVersion,
        new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero),
        "snapshot",
        432,
        1,
        new PublicHistoryFiles());

    private static GroupMap Map() => new(
        2,
        "2026-09-02",
        [new("food", "Продукты")],
        [new("Бакалея", "/business/projects/grocery/", ["food"])]);

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, JsonDefaults.Options), Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
