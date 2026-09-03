using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class WindowPlacementStoreTests
{
    [Fact]
    public async Task SaveAsync_PersistsWindowPlacementForTheNextLaunch()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowPlacementStore(new AppPaths(root), new AtomicFileWriter());
            var expected = new WindowPlacementState(120, 80, 1680, 1050, true);

            await store.SaveAsync(expected, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(expected, loaded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
