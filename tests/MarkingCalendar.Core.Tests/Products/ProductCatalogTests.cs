using MarkingCalendar.Core.Products;

namespace MarkingCalendar.Core.Tests.Products;

public sealed class ProductCatalogTests
{
    [Fact]
    public void EnsureValid_AcceptsOfficialCatalog()
    {
        ProductCatalogValidator.EnsureValid(Catalog());
    }

    [Fact]
    public void EnsureValid_RejectsDuplicateGroupsAndForeignSources()
    {
        var group = Catalog().Groups[0];
        var catalog = new ProductCatalog(1, "r1", [group, group with { SourceUrl = "https://example.com/list" }]);

        var error = Assert.Throws<ProductCatalogValidationException>(() => ProductCatalogValidator.EnsureValid(catalog));

        Assert.Contains(error.Errors, item => item.Contains("повторяется", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(error.Errors, item => item.Contains("Честный знак", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureValid_AcceptsRowsWithoutStage()
    {
        var group = Catalog().Groups[0];
        ProductCatalogValidator.EnsureValid(Catalog() with { Groups = [group with { Rows = [group.Rows[0] with { Section = "" }] }] });
    }

    [Fact]
    public void EnsureValid_RejectsIncompleteNestedJson()
    {
        var group = Catalog().Groups[0];
        ProductCatalog[] invalid = [
            Catalog() with { Groups = null! }, Catalog() with { Groups = [null!] },
            Catalog() with { Groups = [group with { Rows = null! }] },
            Catalog() with { Groups = [group with { Rows = [null!] }] },
            Catalog() with { Groups = [group with { Examples = null!, Rows = [group.Rows[0] with { Meanings = null! }] }] },
            Catalog() with { Groups = [group with { Rows = [group.Rows[0] with { Meanings = [null!] }] }] }
        ];
        foreach (var catalog in invalid)
            Assert.Throws<ProductCatalogValidationException>(() => ProductCatalogValidator.EnsureValid(catalog));
    }

    internal static ProductCatalog Catalog() => new(1, "r1",
    [
        new ProductListGroup("homeware", "Товары для дома", "https://честныйзнак.рф/business/projects/homeware/marking_goods/",
            "Перечень", "abc", "g1", new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero),
            "Условия", ["Посуда"],
            [new ProductListRow("Этап", "Посуда", "6911", "23.41", "Кроме детской",
                [new ProductCodeMeaning("6911", "Посуда столовая", ["тарелки"], "https://eec.eaeunion.org/comission/department/catr/ett/", "Из фарфора")])])
    ]);
}
