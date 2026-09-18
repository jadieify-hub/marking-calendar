using System.Net;
using System.Text.Json;
using MarkingCalendar.Core.Products;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Source;

public sealed class ProductCatalogException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ProductCatalogClient(HttpClient httpClient, string version = "0.1.6")
{
    public static readonly Uri Url = new("https://raw.githubusercontent.com/jadieify-hub/marking-calendar/data/products.json");
    private const int Limit = 5 * 1024 * 1024;

    public async Task<ProductCatalog?> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.UserAgent.ParseAdd($"MarkingCalendar/{version}");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new ProductCatalogException("Перенаправление товарного справочника запрещено.");
            if (!response.IsSuccessStatusCode) throw new ProductCatalogException($"GitHub вернул HTTP {(int)response.StatusCode} при загрузке товарного справочника.");
            if (response.Content.Headers.ContentLength is > Limit) throw new ProductCatalogException("Превышен допустимый размер товарного справочника.");
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > Limit) throw new ProductCatalogException("Превышен допустимый размер товарного справочника.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }
            buffer.Position = 0;
            var catalog = await JsonSerializer.DeserializeAsync<ProductCatalog>(buffer, JsonDefaults.Options, timeout.Token).ConfigureAwait(false);
            ProductCatalogValidator.EnsureValid(catalog);
            return catalog;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ProductCatalogException) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException or ProductCatalogValidationException)
        { throw new ProductCatalogException("Не удалось загрузить товарный справочник.", error); }
    }
}
