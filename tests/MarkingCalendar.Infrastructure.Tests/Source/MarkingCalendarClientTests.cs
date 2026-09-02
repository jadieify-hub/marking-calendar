using System.Net;
using System.Text;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Source;

namespace MarkingCalendar.Infrastructure.Tests.Source;

public sealed class MarkingCalendarClientTests
{
    [Fact]
    public async Task FetchAsync_NormalizesCompleteSourceResponse()
    {
        const string json = """
            {
              "data": {
                "items": [{
                  "date_start": "01.09.2026",
                  "date_end": "",
                  "date_period_text": "с 1 сентября 2026",
                  "tg_name": "Детские игрушки",
                  "event": "Розничная продажа",
                  "stage": "Старт",
                  "description": "Описание",
                  "tg_link": "/business/projects/children/"
                }]
              }
            }
            """;
        using var http = Client(HttpStatusCode.OK, json);
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());

        var snapshot = await client.FetchAsync(CancellationToken.None);

        var item = Assert.Single(snapshot.Events);
        Assert.Equal(new DateOnly(2026, 9, 1), item.Start);
        Assert.Equal("https://честныйзнак.рф/business/projects/children/", item.Url?.AbsoluteUri);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero), snapshot.RetrievedAt);
    }

    [Fact]
    public async Task FetchWithRawAsync_ReturnsOriginalPayloadWithSnapshot()
    {
        const string json = """
            {"data":{"items":[{"date_start":"01.09.2026","tg_name":"Игрушки","event":"Маркировка","stage":"Старт"}]}}
            """;
        using var http = Client(HttpStatusCode.OK, json);
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());

        var result = await client.FetchWithRawAsync(CancellationToken.None);

        Assert.Equal(json, result.RawJson);
        Assert.Single(result.Snapshot.Events);
    }

    [Fact]
    public async Task FetchAsync_RejectsResponseWithoutItems()
    {
        using var http = Client(HttpStatusCode.OK, "{\"data\":{}}");
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());

        var error = await Assert.ThrowsAsync<CalendarSourceException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Equal(CalendarSourceError.InvalidPayload, error.Code);
    }

    [Fact]
    public async Task FetchWithRawAsync_PreservesRejectedRawPayload()
    {
        const string json = "{\"data\":{}}";
        using var http = Client(HttpStatusCode.OK, json);
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());

        var error = await Assert.ThrowsAsync<CalendarSourceException>(() => client.FetchWithRawAsync(CancellationToken.None));

        Assert.Equal(CalendarSourceError.InvalidPayload, error.Code);
        Assert.Equal(json, error.RawJson);
    }

    [Fact]
    public async Task FetchAsync_ReportsHttpFailure()
    {
        using var http = Client(HttpStatusCode.BadGateway, "gateway error");
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());

        var error = await Assert.ThrowsAsync<CalendarSourceException>(() => client.FetchAsync(CancellationToken.None));

        Assert.Equal(CalendarSourceError.HttpFailure, error.Code);
    }

    [Fact]
    public async Task FetchAsync_UsesConfiguredEndpoint()
    {
        var expected = new Uri("https://example.test/custom-calendar");
        Uri? requested = null;
        using var http = new HttpClient(new DelegateHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":{\"items\":[{\"date_start\":\"01.09.2026\",\"tg_name\":\"Игрушки\",\"event\":\"Маркировка\",\"stage\":\"Старт\"}]}}")
            });
        }));
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider(), endpoint: expected);

        await client.FetchAsync(CancellationToken.None);

        Assert.Equal(expected, requested);
    }

    [Fact]
    public async Task FetchAsync_PropagatesCancellation()
    {
        using var http = new HttpClient(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var client = new MarkingCalendarClient(http, new EventNormalizer(), new FixedTimeProvider());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(cancellation.Token));
    }

    private static HttpClient Client(HttpStatusCode status, string content) => new(new DelegateHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        })));

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
    }
}
