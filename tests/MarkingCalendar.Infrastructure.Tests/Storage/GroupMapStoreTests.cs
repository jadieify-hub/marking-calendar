using MarkingCalendar.Core.Groups;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class GroupMapStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsValidatedMap()
    {
        using var temp = new TemporaryDirectory();
        var store = new GroupMapStore(new AppPaths(temp.Path), new AtomicFileWriter());
        var map = Map();

        await store.SaveAsync(map, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(map.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal("food", Assert.Single(loaded.Sectors).Id);
        Assert.Equal("Бакалея", Assert.Single(loaded.Groups).Name);
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidSavedMap()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.GroupMapFile, "{\"schemaVersion\":1}");
        var store = new GroupMapStore(paths, new AtomicFileWriter());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(CancellationToken.None));
    }

    private static GroupMap Map() => new(
        2,
        "2026-09-02",
        [new("food", "Продукты")],
        [new("Бакалея", "/business/projects/grocery/", ["food"])]);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkingCalendar.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
