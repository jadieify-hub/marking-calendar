using System.Reflection;
using System.Collections.Concurrent;
using System.Windows.Threading;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.App.Web;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;
using MarkingCalendar.Infrastructure.Updates;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class AppBootstrapperTests
{
    [Fact]
    public async Task RefreshAsync_FromBackgroundThread_PresentsFoundChanges()
    {
        await using var fixture = await Fixture.CreateAsync(new FixedSource());

        await Task.Run(() => fixture.RefreshAsync()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("updated", fixture.Read<AppStatusViewModel>("_status").Kind);
        var notice = Assert.IsType<ChangeBatch>(fixture.Read<ChangeBatch?>("_notice"));
        Assert.Single(notice.Changes.Moved);
        Assert.Equal(notice.Id, Assert.Single((await fixture.Store.LoadHistoryAsync(CancellationToken.None)).Batches).Id);
    }

    [Fact]
    public async Task RefreshAsync_UnchangedSnapshotKeepsUnreadNoticeAndComparison()
    {
        await using var fixture = await Fixture.CreateAsync(new FixedSource());
        await fixture.RefreshAsync();
        var notice = fixture.Read<ChangeBatch>("_notice");
        var comparison = new SnapshotComparison(Snapshot().RetrievedAt,
            new ChangeSummaryFactory().Create(ChangeSet.Empty, 0, new DateOnly(2026, 9, 5), new HashSet<string>()));
        fixture.Set("_comparison", comparison);

        await fixture.RefreshAsync();

        Assert.Equal("ready", fixture.Read<AppStatusViewModel>("_status").Kind);
        Assert.Same(notice, fixture.Read<ChangeBatch>("_notice"));
        Assert.Contains(notice.Id, fixture.Read<IReadOnlyList<string>>("_noticeRelatedBatchIds"));
        Assert.Same(comparison, fixture.Read<SnapshotComparison>("_comparison"));
    }

    [Fact]
    public async Task RefreshAsync_SkipsOverlappingRequests()
    {
        var source = new PausedSource();
        await using var fixture = await Fixture.CreateAsync(source);
        var first = fixture.RefreshAsync();
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = fixture.RefreshAsync();
        await fixture.OnUiAsync(() => { });
        source.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, source.Calls);
        Assert.NotNull(fixture.Read<ChangeBatch?>("_notice"));
    }

    [Fact]
    public async Task ReadyAsync_RechecksWhileOpen_AndCancelsOnClose()
    {
        var source = new PausedSource(skipFirst: true);
        await using var fixture = await Fixture.CreateAsync(source);
        await fixture.InvokeAsync("ReadyAsync");
        try
        {
            await fixture.OnUiAsync(() => fixture.Read<DispatcherTimer>("_refreshTimer").Interval = TimeSpan.FromMilliseconds(10));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.DisposeHostAsync();
            await source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Read<Task>("_refreshTask").WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.RefreshAsync();

            Assert.Equal(2, source.Calls);
            Assert.Empty(fixture.Logger.Errors);
        }
        finally
        {
            source.Release.TrySetResult();
        }
    }

    [Fact]
    public async Task RefreshAsync_ReportsHistoryWriteFailure_AndAllowsRetry()
    {
        await using var fixture = await Fixture.CreateAsync(new FixedSource(), new FailingMergeWriter());

        await fixture.RefreshAsync();

        Assert.Equal("error", fixture.Read<AppStatusViewModel>("_status").Kind);
        Assert.IsType<IOException>(Assert.Single(fixture.Logger.Errors));
        Assert.Equal(Snapshot(moved: true).Id, (await fixture.Store.LoadCurrentAsync(CancellationToken.None))?.Id);

        await fixture.RefreshAsync();
        Assert.Equal("ready", fixture.Read<AppStatusViewModel>("_status").Kind);
    }

    private static CalendarSnapshot Snapshot(bool moved = false)
    {
        var date = new DateOnly(2026, moved ? 11 : 10, 1);
        return CalendarSnapshot.Create(
            new DateTimeOffset(2026, 9, 5, moved ? 8 : 7, 0, 0, TimeSpan.Zero),
            new Uri("https://example.test/calendar"),
            [new CalendarEvent(moved ? "moved" : "original", date, null, "с октября", "Бакалея", "Маркировка", "Старт", "Описание", null)]);
    }

    private sealed class FixedSource : ICalendarSource
    {
        public Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot(moved: true));
    }

    private sealed class PausedSource(bool skipFirst = false) : ICalendarSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1 && skipFirst) return Snapshot(moved: true);
            Entered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
            return Snapshot(moved: true);
        }
    }

    private sealed class FailingMergeWriter : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _writer = new();
        private int _historyWrites;

        public Task WriteJsonAsync<T>(string destination, T value, CancellationToken cancellationToken)
        {
            // The update service already saved the batch; fail the host's subsequent history merge.
            if (value is ChangeHistory && ++_historyWrites == 2) throw new IOException("History write failed.");
            return _writer.WriteJsonAsync(destination, value, cancellationToken);
        }

        public Task WriteTextAsync(string destination, string value, CancellationToken cancellationToken) =>
            _writer.WriteTextAsync(destination, value, cancellationToken);
    }

    // Exercise the real host without starting WebView2, accessing the network or loading the user's profile.
    // Reflection stays in this fixture; the production API does not gain test-only initialization methods.
    private sealed class Fixture : IAsyncDisposable
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly string _root;
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private readonly AppBootstrapper _bootstrapper;

        private Fixture(string root, Thread thread, Dispatcher dispatcher, AppBootstrapper bootstrapper, CalendarStore store, RecordingLogger logger)
        {
            _root = root;
            _thread = thread;
            _dispatcher = dispatcher;
            _bootstrapper = bootstrapper;
            Store = store;
            Logger = logger;
        }

        public CalendarStore Store { get; }
        public RecordingLogger Logger { get; }

        public static async Task<Fixture> CreateAsync(ICalendarSource source, IAtomicFileWriter? writer = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var store = new CalendarStore(paths, new SnapshotValidator(), writer ?? new AtomicFileWriter());
            var logger = new RecordingLogger();
            await store.SaveValidatedAsync(Snapshot(), CancellationToken.None);
            var ready = new TaskCompletionSource<(Dispatcher Dispatcher, AppBootstrapper Bootstrapper)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var window = new MainWindow(paths.BrowserDataDirectory);
                    var bootstrapper = new AppBootstrapper(window, logger);
                    void Set(string name, object value) => typeof(AppBootstrapper).GetField(name, PrivateInstance)!.SetValue(bootstrapper, value);
                    Set("_store", store);
                    Set("_snapshot", Snapshot());
                    Set("_state", AppState.Initial.WithChangeNotifications(false));
                    Set("_viewModelFactory", new AppViewModelFactory(new ChangeSummaryFactory(), TimeProvider.System));
                    Set("_updatePresentationPolicy", new UpdatePresentationPolicy(new ChangeSummaryFactory(), TimeProvider.System));
                    Set("_updateService", new CalendarUpdateService(source, store, new EventDiffEngine(), TimeProvider.System));
                    ready.SetResult((window.Dispatcher, bootstrapper));
                    Dispatcher.Run();
                }
                catch (Exception error)
                {
                    ready.TrySetException(error);
                }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var initialized = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return new Fixture(root, thread, initialized.Dispatcher, initialized.Bootstrapper, store, logger);
        }

        public T Read<T>(string name) => (T)typeof(AppBootstrapper).GetField(name, PrivateInstance)!.GetValue(_bootstrapper)!;

        public void Set(string name, object value) => typeof(AppBootstrapper).GetField(name, PrivateInstance)!.SetValue(_bootstrapper, value);

        public Task RefreshAsync() => InvokeAsync("RefreshAsync");

        public Task InvokeAsync(string method) => (Task)typeof(AppBootstrapper)
            .GetMethod(method, PrivateInstance)!.Invoke(_bootstrapper, [CancellationToken.None])!;

        public Task OnUiAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

        public Task DisposeHostAsync() => OnUiAsync(_bootstrapper.Dispose);

        public async ValueTask DisposeAsync()
        {
            await DisposeHostAsync();
            if (typeof(AppBootstrapper).GetField("_refreshTask", PrivateInstance)?.GetValue(_bootstrapper) is Task pending)
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }
            _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public void Log(AppLogLevel level, string source, string message, Exception? exception = null)
        {
            if (exception is not null) Errors.Enqueue(exception);
        }
        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
