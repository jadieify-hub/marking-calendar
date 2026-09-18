using System.Text.Json;
using MarkingCalendar.Core.Products;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed class ProductCatalogStore(AppPaths paths, IAtomicFileWriter writer)
{
    public async Task<ProductCatalog?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.ProductCatalogFile)) return null;
        try
        {
            await using var stream = File.OpenRead(paths.ProductCatalogFile);
            var catalog = await JsonSerializer.DeserializeAsync<ProductCatalog>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            ProductCatalogValidator.EnsureValid(catalog);
            return catalog;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ProductCatalogValidationException)
        {
            throw new InvalidDataException("Сохранённый товарный справочник повреждён.", error);
        }
    }

    public Task SaveAsync(ProductCatalog catalog, CancellationToken cancellationToken)
    {
        ProductCatalogValidator.EnsureValid(catalog);
        return writer.WriteJsonAsync(paths.ProductCatalogFile, catalog, cancellationToken);
    }
}
