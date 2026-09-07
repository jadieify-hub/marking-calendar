using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class AppStateStoreTests
{
    [Fact]
    public async Task LoadAsync_MigratesVersionOneLastShownBatchToSeenIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            await File.WriteAllTextAsync(paths.StateFile, "{\"version\":1,\"lastShownBatchId\":\"batch-42\",\"selectedGroups\":[\" Радиоэлектроника \"]}");

            var loaded = await new AppStateStore(paths, new AtomicFileWriter()).LoadAsync(CancellationToken.None);

            Assert.Equal(6, loaded.Version);
            Assert.Equal(["batch-42"], loaded.SeenBatchIds);
            Assert.Equal(["радиоэлектроника"], loaded.SelectedGroups);
            Assert.Equal("auto", loaded.Theme);
            Assert.True(loaded.PublicHistoryEnabled);
            Assert.True(loaded.ChangeNotificationsEnabled);
            Assert.Null(loaded.LastPublicHistorySync);
            Assert.True(loaded.OnboardingCompleted);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsNormalizedCurrentState()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var store = new AppStateStore(paths, new AtomicFileWriter());
            await store.SaveAsync(
                new AppState(
                    3,
                    ["batch-2", "batch-2", "batch-1"],
                    ["Обувь", " Обувь ", "Игрушки", "Радиоэлектроника\u00a0"],
                    "dark",
                    false,
                    new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero)).WithChangeNotifications(false),
                CancellationToken.None);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(["batch-2", "batch-1"], loaded.SeenBatchIds);
            Assert.Equal(["игрушки", "обувь", "радиоэлектроника"], loaded.SelectedGroups);
            Assert.Empty(loaded.HiddenGroupSuggestions);
            Assert.Equal("dark", loaded.Theme);
            Assert.False(loaded.PublicHistoryEnabled);
            Assert.False(loaded.ChangeNotificationsEnabled);
            Assert.Equal(new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero), loaded.LastPublicHistorySync);
            Assert.True(loaded.OnboardingCompleted);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_PreservesCallOrderWhenFirstWriteIsDelayed()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var writer = new DelayedFirstWriter();
            var store = new AppStateStore(paths, writer);
            var older = AppState.Initial.WithProfile(
                ["retail"],
                ["pharmacy"],
                new Dictionary<string, bool> { ["Обувь"] = true },
                ["Обувь"]);
            var newer = AppState.Initial.WithProfile(
                ["producer"],
                ["food"],
                new Dictionary<string, bool> { ["Игрушки"] = true },
                ["Игрушки"]);

            var first = store.SaveAsync(older, CancellationToken.None);
            var second = store.SaveAsync(newer, CancellationToken.None);
            if (writer.WriteCount == 1) writer.ReleaseFirst();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(["producer"], loaded.Roles);
            Assert.Equal(["food"], loaded.SelectedSectors);
            Assert.Equal(["игрушки"], loaded.SelectedGroups);
            Assert.Single(loaded.ManualGroups);
            Assert.True(loaded.ManualGroups["игрушки"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferenceUpdates_PreserveOtherStateAndNormalizeValues()
    {
        var initial = new AppState(2, ["batch-1"], ["Старая"], "auto");

        var updated = initial.WithGroups([" Обувь ", "обувь", "Игрушки"]).WithTheme("light").WithChangeNotifications(false);

        Assert.Equal(["batch-1"], updated.SeenBatchIds);
        Assert.Equal(["игрушки", "обувь"], updated.SelectedGroups);
        Assert.Equal("light", updated.Theme);
        Assert.True(updated.PublicHistoryEnabled);
        Assert.False(updated.ChangeNotificationsEnabled);
    }

    [Fact]
    public async Task LoadAsync_LeavesOnboardingPendingForLegacyStateWithoutSelectedGroups()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            await File.WriteAllTextAsync(paths.StateFile, "{\"version\":4,\"selectedGroups\":[]}");

            var loaded = await new AppStateStore(paths, new AtomicFileWriter()).LoadAsync(CancellationToken.None);

            Assert.False(loaded.OnboardingCompleted);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_QuarantinesCorruptJsonAndAllowsNextSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            await File.WriteAllTextAsync(paths.StateFile, "{\"version\":");
            var store = new AppStateStore(paths, new AtomicFileWriter());

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(AppState.Initial, loaded);
            Assert.False(File.Exists(paths.StateFile));
            Assert.Single(Directory.GetFiles(root, "state.corrupt-*.json"));

            await store.SaveAsync(AppState.Initial.WithTheme("dark"), CancellationToken.None);

            Assert.Equal("dark", (await store.LoadAsync(CancellationToken.None)).Theme);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_DoesNotHideIoFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            await using (var locked = new FileStream(
                paths.StateFile,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                await Assert.ThrowsAsync<IOException>(
                    () => new AppStateStore(paths, new AtomicFileWriter()).LoadAsync(CancellationToken.None));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class DelayedFirstWriter : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();
        private readonly TaskCompletionSource<bool> _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;

        public int WriteCount => Volatile.Read(ref _writeCount);

        public void ReleaseFirst() => _releaseFirst.TrySetResult(true);

        public async Task WriteJsonAsync<T>(string destination, T value, CancellationToken cancellationToken)
        {
            var writeNumber = Interlocked.Increment(ref _writeCount);
            if (writeNumber == 1)
            {
                await _releaseFirst.Task.WaitAsync(cancellationToken);
            }

            await _inner.WriteJsonAsync(destination, value, cancellationToken);
            if (writeNumber == 2) ReleaseFirst();
        }

        public Task WriteTextAsync(string destination, string value, CancellationToken cancellationToken) =>
            _inner.WriteTextAsync(destination, value, cancellationToken);
    }
}
