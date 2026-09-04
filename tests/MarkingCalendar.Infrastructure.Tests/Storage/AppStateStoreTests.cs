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
}
