using MarkingCalendar.App.Updates;
using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.App.Tests.Updates;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckAndDownloadAsync_ReportsNoUpdate()
    {
        var source = new FakeUpdateSource { Available = null };
        var service = new AppUpdateService(source);

        await service.CheckAndDownloadAsync(CancellationToken.None);

        Assert.Equal(AppUpdateStage.NoUpdate, service.State.Stage);
        Assert.Equal("Установлена последняя версия", service.State.Message);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_ReportsProgressAndReadyToRestart()
    {
        var source = new FakeUpdateSource
        {
            Available = new AppUpdateRelease("0.2.0", new object()),
            ProgressValues = [20, 75, 100]
        };
        var service = new AppUpdateService(source);
        var states = new List<AppUpdateState>();
        service.StateChanged += (_, state) => states.Add(state);

        await service.CheckAndDownloadAsync(CancellationToken.None);

        Assert.Contains(states, state => state.Stage == AppUpdateStage.Downloading && state.Progress == 75);
        Assert.Equal(AppUpdateStage.ReadyToRestart, service.State.Stage);
        Assert.Equal("0.2.0", service.State.Version);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_CoalescesSmallFrequentProgressUpdates()
    {
        var timeProvider = new AdjustableTimeProvider();
        var source = new FakeUpdateSource
        {
            Available = new AppUpdateRelease("0.2.0", new object()),
            ProgressValues = [2, 4, 5, 6],
            BeforeProgress = value => timeProvider.Advance(value == 6 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(100))
        };
        var service = new AppUpdateService(source, timeProvider: timeProvider);
        var progress = new List<int>();
        service.StateChanged += (_, state) =>
        {
            if (state.Stage == AppUpdateStage.Downloading && state.Progress is { } value) progress.Add(value);
        };

        await service.CheckAndDownloadAsync(CancellationToken.None);

        Assert.Equal([0, 5, 6], progress);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_KeepsFailureNonFatal()
    {
        var source = new FakeUpdateSource { Error = new HttpRequestException("offline") };
        var logger = new RecordingLogger();
        var service = new AppUpdateService(source, logger);

        await service.CheckAndDownloadAsync(CancellationToken.None);

        Assert.Equal(AppUpdateStage.Failed, service.State.Stage);
        Assert.Equal("Не удалось проверить обновление приложения", service.State.Message);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal("app-update", logged.Source);
        Assert.IsType<HttpRequestException>(logged.Exception);
    }

    [Fact]
    public async Task ApplyAndRestart_UsesDownloadedReleaseOnlyWhenReady()
    {
        var source = new FakeUpdateSource { Available = new AppUpdateRelease("0.2.0", new object()) };
        var service = new AppUpdateService(source);
        await service.CheckAndDownloadAsync(CancellationToken.None);

        var applied = service.ApplyAndRestart();

        Assert.True(applied);
        Assert.Same(source.Available, source.Applied);
    }

    private sealed class FakeUpdateSource : IAppUpdateSource
    {
        public AppUpdateRelease? Available { get; init; }
        public Exception? Error { get; init; }
        public int[] ProgressValues { get; init; } = [100];
        public Action<int>? BeforeProgress { get; init; }
        public AppUpdateRelease? Applied { get; private set; }

        public Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
        {
            if (Error is not null) throw Error;
            return Task.FromResult(Available);
        }

        public Task DownloadAsync(AppUpdateRelease release, IProgress<int> progress, CancellationToken cancellationToken)
        {
            foreach (var value in ProgressValues)
            {
                BeforeProgress?.Invoke(value);
                progress.Report(value);
            }
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(AppUpdateRelease release) => Applied = release;
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(string Source, Exception? Exception)> Entries { get; } = [];

        public void Log(AppLogLevel level, string source, string message, Exception? exception = null) =>
            Entries.Add((source, exception));

        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
