using System.Text.Json;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Source;

public sealed class CalendarPayloadParser(IEventNormalizer normalizer)
{
    private readonly IEventNormalizer _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));

    public async Task<IReadOnlyList<CalendarEvent>> ParseAsync(Stream json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        SourceResponse? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<SourceResponse>(json, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new CalendarSourceException(CalendarSourceError.InvalidPayload, "Источник вернул повреждённый JSON.", error);
        }

        if (payload?.Data?.Items is null)
        {
            throw new CalendarSourceException(CalendarSourceError.InvalidPayload, "В ответе источника отсутствует data.items.");
        }

        try
        {
            return payload.Data.Items.Select(item => _normalizer.Normalize(new SourceEvent(
                item.DateStart,
                item.DateEnd,
                item.Period,
                item.Group,
                item.Type,
                item.Stage,
                item.Description,
                item.Url))).ToArray();
        }
        catch (CalendarEventValidationException error)
        {
            throw new CalendarSourceException(CalendarSourceError.InvalidPayload, "Источник вернул некорректное событие.", error);
        }
    }
}

