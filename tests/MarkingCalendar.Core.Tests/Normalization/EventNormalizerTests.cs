using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Normalization;

public sealed class EventNormalizerTests
{
    [Fact]
    public void Normalize_ConvertsSourceFieldsToStableEvent()
    {
        var source = new SourceEvent(
            "01.09.2026",
            "",
            "с 1 сентября 2026",
            "Антисептики и&nbsp;дезинфицирующие средства",
            "Розничная продажа",
            "Старт",
            "Описание",
            "/business/projects/children/");

        var actual = new EventNormalizer().Normalize(source);

        Assert.Equal(new DateOnly(2026, 9, 1), actual.Start);
        Assert.Null(actual.End);
        Assert.Equal("Антисептики и дезинфицирующие средства", actual.Group);
        Assert.Equal("https://честныйзнак.рф/business/projects/children/", actual.Url?.AbsoluteUri);
        Assert.False(string.IsNullOrWhiteSpace(actual.Id));
    }

    [Fact]
    public void Normalize_RejectsMissingRequiredFields()
    {
        var source = new SourceEvent("2026-09-01", "", "", "", "Маркировка", "Старт", "", "");

        var error = Assert.Throws<CalendarEventValidationException>(() => new EventNormalizer().Normalize(source));

        Assert.Contains("товарная группа", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
