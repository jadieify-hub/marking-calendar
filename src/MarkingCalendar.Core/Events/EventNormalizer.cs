using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace MarkingCalendar.Core.Events;

public sealed partial class EventNormalizer : IEventNormalizer
{
    private static readonly Uri SourceRoot = new("https://честныйзнак.рф/");
    private static readonly string[] AcceptedDateFormats = ["dd.MM.yyyy", "yyyy-MM-dd"];

    public CalendarEvent Normalize(SourceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var group = Decode(source.Group);
        var type = Decode(source.Type);
        var stage = Decode(source.Stage);

        Require(group, "Товарная группа");
        Require(type, "Тип события");
        Require(stage, "Этап");

        var start = ParseDate(source.DateStart, "дата начала");
        var end = ParseDate(source.DateEnd, "дата окончания");
        if (start is null && end is null)
        {
            throw new CalendarEventValidationException("Не указана дата начала или окончания события.");
        }

        var period = Decode(source.Period);
        var description = Decode(source.Description);
        var url = ParseUrl(source.Url);
        var canonical = string.Join('|',
            start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            period,
            group,
            type,
            stage,
            description,
            url?.AbsoluteUri ?? "");

        return new CalendarEvent(
            EventId.FromCanonicalContent(canonical),
            start,
            end,
            period,
            group,
            type,
            stage,
            description,
            url);
    }

    private static string Decode(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty).Replace('\u00A0', ' ');
        return Whitespace().Replace(decoded, " ").Trim();
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CalendarEventValidationException($"{field} не указана.");
        }
    }

    private static DateOnly? ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value.Trim(),
                AcceptedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new CalendarEventValidationException($"Некорректная {field}: {value}.");
    }

    private static Uri? ParseUrl(string? value)
    {
        var decoded = Decode(value);
        if (string.IsNullOrEmpty(decoded))
        {
            return null;
        }

        if (decoded.StartsWith('/'))
        {
            return new Uri(SourceRoot, decoded);
        }

        if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return uri;
        }

        throw new CalendarEventValidationException($"Некорректная ссылка события: {decoded}.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
