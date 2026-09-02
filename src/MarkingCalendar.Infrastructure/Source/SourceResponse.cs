using System.Text.Json.Serialization;

namespace MarkingCalendar.Infrastructure.Source;

internal sealed record SourceResponse([property: JsonPropertyName("data")] SourceData? Data);

internal sealed record SourceData([property: JsonPropertyName("items")] IReadOnlyList<SourceItem>? Items);

internal sealed record SourceItem(
    [property: JsonPropertyName("date_start")] string? DateStart,
    [property: JsonPropertyName("date_end")] string? DateEnd,
    [property: JsonPropertyName("date_period_text")] string? Period,
    [property: JsonPropertyName("tg_name")] string? Group,
    [property: JsonPropertyName("event")] string? Type,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("tg_link")] string? Url);

