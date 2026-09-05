using MarkingCalendar.App.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace MarkingCalendar.App.Tests.Updates;

public sealed class VelopackUpdateSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckAsync_UsesPrimaryWithoutFallbackOnSuccess(bool hasUpdate)
    {
        var primary = new RecordingManager { Available = hasUpdate ? Release("0.2.0") : null };
        var fallback = new RecordingManager { Error = new HttpRequestException("must not be called") };
        var source = new VelopackUpdateSource(primary: primary, fallback: fallback);

        var release = await source.CheckAsync(CancellationToken.None);

        Assert.Equal(hasUpdate ? "0.2.0" : null, release?.Version);
        Assert.Equal(1, primary.Checks);
        Assert.Equal(0, fallback.Checks);
        if (release is not null)
        {
            await source.DownloadAsync(release, new Progress<int>(), CancellationToken.None);
            Assert.Same(primary.Available, primary.Downloaded);
            Assert.Null(fallback.Downloaded);
        }
    }

    [Fact]
    public async Task CheckAsync_FallbackReleaseKeepsItsDownloadSourceAfterAnotherCheck()
    {
        var primary = new RecordingManager { Error = new HttpRequestException("raw unavailable") };
        var fallback = new RecordingManager { Available = Release("0.2.0") };
        var source = new VelopackUpdateSource(primary: primary, fallback: fallback);
        var release = await source.CheckAsync(CancellationToken.None);
        Assert.Equal("0.2.0", release?.Version);

        primary.Error = null;
        primary.Available = Release("0.3.0");
        await source.CheckAsync(CancellationToken.None);
        await source.DownloadAsync(release!, new Progress<int>(), CancellationToken.None);

        Assert.Equal(2, primary.Checks);
        Assert.Equal(1, fallback.Checks);
        Assert.Same(fallback.Available, fallback.Downloaded);
        Assert.Null(primary.Downloaded);
    }

    [Fact]
    public async Task CheckAsync_TriesEachSourceOnceWhenBothFail()
    {
        var primary = new RecordingManager { Error = new HttpRequestException("raw unavailable") };
        var fallback = new RecordingManager { Error = new HttpRequestException("github unavailable") };
        var source = new VelopackUpdateSource(primary: primary, fallback: fallback);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => source.CheckAsync(CancellationToken.None));

        Assert.Same(fallback.Error, error);
        Assert.Equal(1, primary.Checks);
        Assert.Equal(1, fallback.Checks);
    }

    [Fact]
    public async Task CheckAsync_DoesNotFallBackWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var primary = new RecordingManager
        {
            BeforeCheck = cancellation.Cancel,
            Error = new OperationCanceledException(cancellation.Token)
        };
        var fallback = new RecordingManager();
        var source = new VelopackUpdateSource(primary: primary, fallback: fallback);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.CheckAsync(cancellation.Token));

        Assert.Equal(1, primary.Checks);
        Assert.Equal(0, fallback.Checks);
    }

    private static UpdateInfo Release(string version) => new(new VelopackAsset
    {
        PackageId = "MarkingCalendar", Version = SemanticVersion.Parse(version),
        Type = VelopackAssetType.Full, FileName = $"MarkingCalendar-{version}-full.nupkg"
    }, false);

    private sealed class RecordingManager() : UpdateManager(
        new SimpleWebSource("https://updates.example.invalid/"),
        locator: new TestVelopackLocator("MarkingCalendar", "0.1.13", Path.GetTempPath()))
    {
        public UpdateInfo? Available { get; set; }
        public Exception? Error { get; set; }
        public Action? BeforeCheck { get; init; }
        public int Checks { get; private set; }
        public UpdateInfo? Downloaded { get; private set; }

        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            Checks++;
            BeforeCheck?.Invoke();
            if (Error is not null) throw Error;
            return Task.FromResult(Available);
        }

        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            Downloaded = updates;
            return Task.CompletedTask;
        }
    }
}
