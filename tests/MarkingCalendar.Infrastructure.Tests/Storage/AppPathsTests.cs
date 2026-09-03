using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class AppPathsTests
{
    [Fact]
    public void ForCurrentUser_UsesPublisherDirectorySeparateFromInstallation()
    {
        var paths = AppPaths.ForCurrentUser();

        Assert.EndsWith(
            Path.Combine("KRS", "MarkingCalendar"),
            paths.RootDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserDataDirectory_StaysInsideWritableApplicationStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "marking-calendar-paths");
        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "webview2"), paths.BrowserDataDirectory);
    }
}
