using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using System.Text;

namespace MarkingCalendar.Infrastructure.Source;

public sealed class MarkingCalendarClient(
    HttpClient httpClient,
    IEventNormalizer normalizer,
    TimeProvider timeProvider,
    string version = "0.1.0",
    Uri? endpoint = null) : IRawCalendarSource
{
    public static Uri Endpoint { get; } = new("https://xn--80ajghhoc2aj1c8b.xn--p1ai/bitrix/services/main/ajax.php?mode=class&c=dev%3AmarkingCalendar&action=getSheduleList");

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly CalendarPayloadParser _parser = new(normalizer ?? throw new ArgumentNullException(nameof(normalizer)));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly string _version = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version;
    private readonly Uri _endpoint = endpoint ?? Endpoint;

    public async Task<CalendarSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var payload = await FetchWithRawAsync(cancellationToken).ConfigureAwait(false);
        return payload.Snapshot;
    }

    public async Task<CalendarSourcePayload> FetchWithRawAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.UserAgent.ParseAdd($"MarkingCalendar/{_version}");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            throw new CalendarSourceException(CalendarSourceError.NetworkFailure, "Не удалось связаться с источником календаря.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CalendarSourceException(
                    CalendarSourceError.HttpFailure,
                    $"Источник календаря вернул HTTP {(int)response.StatusCode}.");
            }

            var rawJson = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawJson), writable: false);
            IReadOnlyList<CalendarEvent> events;
            try
            {
                events = await _parser.ParseAsync(stream, timeout.Token).ConfigureAwait(false);
            }
            catch (CalendarSourceException error) when (error.Code == CalendarSourceError.InvalidPayload)
            {
                throw new CalendarSourceException(error.Code, error.Message, error, rawJson);
            }

            var snapshot = CalendarSnapshot.Create(_timeProvider.GetUtcNow(), _endpoint, events);
            return new CalendarSourcePayload(snapshot, rawJson);
        }
    }
}
