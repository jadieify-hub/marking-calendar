using System.Net;
using System.Text;
using MarkingCalendar.Infrastructure.Source;

namespace MarkingCalendar.Infrastructure.Tests.Source;

public sealed class ProductCatalogClientTests
{
    [Fact]
    public async Task FetchAsync_ReturnsNullForMissingOldChannel()
    {
        using var http = new HttpClient(new Handler(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var result = await new ProductCatalogClient(http).FetchAsync(CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_RejectsPayloadOverFiveMiB()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[5 * 1024 * 1024 + 1]) };
        using var http = new HttpClient(new Handler(response));
        await Assert.ThrowsAsync<ProductCatalogException>(() => new ProductCatalogClient(http).FetchAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("https://example.com/products.json")]
    [InlineData("/products.json")]
    public async Task FetchAsync_RejectsRedirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
        using var http = new HttpClient(new Handler(response));
        await Assert.ThrowsAsync<ProductCatalogException>(() => new ProductCatalogClient(http).FetchAsync(CancellationToken.None));
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
