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
}
