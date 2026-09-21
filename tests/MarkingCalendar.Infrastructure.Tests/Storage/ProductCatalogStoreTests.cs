using MarkingCalendar.Core.Products;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class ProductCatalogStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsValidatedCatalog()
    {
        var store = new ProductCatalogStore(new AppPaths(_root), new AtomicFileWriter());
        var catalog = Catalog();

        await store.SaveAsync(catalog, CancellationToken.None);

        var loaded = Assert.IsType<ProductCatalog>(await store.LoadAsync(CancellationToken.None));
        Assert.Equal(catalog.Revision, loaded.Revision);
        var group = Assert.Single(loaded.Groups);
        Assert.Equal(catalog.Groups[0].Id, group.Id);
        Assert.Equal(catalog.Groups[0].SourceUrl, group.SourceUrl);
        Assert.Equal(catalog.Groups[0].Rows[0].Meanings[0].Code, Assert.Single(Assert.Single(group.Rows).Meanings).Code);
    }

    [Fact]
    public async Task LoadAsync_ReportsCorruptCacheWithoutDeletingIt()
    {
        var paths = new AppPaths(_root);
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllTextAsync(paths.ProductCatalogFile, "{bad");
        var store = new ProductCatalogStore(paths, new AtomicFileWriter());

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(CancellationToken.None));

        Assert.True(File.Exists(paths.ProductCatalogFile));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static ProductCatalog Catalog() => new(2, "r1", [new ProductListGroup("grocery", "Бакалея",
        "https://честныйзнак.рф/business/projects/grocery/mark_goods/", "Перечень", "hash", "g1",
        new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), "Условия", [],
        [new ProductListRow("Этап", "Чай", "0902", "", "", [new ProductCodeMeaning("0902", "Чай", [],
            "https://eec.eaeunion.org/comission/department/catr/ett/", "Чай")])],
        new ProductScope(["Чай"], "", "https://честныйзнак.рф/business/projects/grocery/mark_goods/"))]);
}
