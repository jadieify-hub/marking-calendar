using MarkingCalendar.Core.Events;
using System.Reflection;
using System.Text.Json;

namespace MarkingCalendar.Core.Tests.Events;

public sealed class EventClassifierTests
{
    [Theory]
    [InlineData("Розничная продажа", "", EventCategory.Retail)]
    [InlineData("Поэкземплярный учет по ЭДО", "", EventCategory.Edo)]
    [InlineData("Объемно-сортовой учет по ЭДО", "", EventCategory.Edo)]
    [InlineData("Партионный учет по ЭДО", "", EventCategory.Edo)]
    [InlineData("Вывод из оборота по иным причинам", "", EventCategory.Edo)]
    [InlineData("Запрет оборота немаркированной продукции", "", EventCategory.Ban)]
    [InlineData("Разрешительный режим", "Старт разрешительного режима на кассах", EventCategory.Permit)]
    [InlineData("Обязательная маркировка (ввод в оборот)", "", EventCategory.Marking)]
    [InlineData("Маркировка остатков", "", EventCategory.Marking)]
    [InlineData("Эксперимент", "Добровольный этап", EventCategory.Marking)]
    [InlineData("Обязательная регистрация", "Старт обязательной регистрации в системе маркировки", EventCategory.Registration)]
    [InlineData("  ПОЭКЗЕМПЛЯРНЫЙ   УЧЁТ ПО ЭДО  ", "", EventCategory.Edo)]
    public void Classify_MapsSourceWordingToStableCategory(string type, string stage, EventCategory expected)
    {
        Assert.Equal(expected, EventClassifier.Classify(type, stage));
    }

    [Theory]
    [InlineData("Другое", "Старт разрешительного режима", EventCategory.Permit)]
    [InlineData("Другое", "Неизвестное изменение", EventCategory.Other)]
    [InlineData("Новый тип", "Обязательная регистрация участников", EventCategory.Registration)]
    [InlineData("Новый тип", "Запрет немаркированной продукции на кассах", EventCategory.Ban)]
    public void Classify_UsesOrderedKeywordsOnlyAsFallback(string type, string stage, EventCategory expected)
    {
        Assert.Equal(expected, EventClassifier.Classify(type, stage));
    }

    [Fact]
    public void Classify_MapsEveryKnownTypeInBundledSnapshotByExactType()
    {
        var expectedByType = new Dictionary<string, EventCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["Розничная продажа"] = EventCategory.Retail,
            ["Поэкземплярный учет по ЭДО"] = EventCategory.Edo,
            ["Объемно-сортовой учет по ЭДО"] = EventCategory.Edo,
            ["Партионный учет по ЭДО"] = EventCategory.Edo,
            ["Вывод из оборота по иным причинам"] = EventCategory.Edo,
            ["Запрет оборота немаркированной продукции"] = EventCategory.Ban,
            ["Разрешительный режим"] = EventCategory.Permit,
            ["Обязательная маркировка (ввод в оборот)"] = EventCategory.Marking,
            ["Маркировка остатков"] = EventCategory.Marking,
            ["Эксперимент"] = EventCategory.Marking,
            ["Обязательная регистрация"] = EventCategory.Registration
        };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "MarkingCalendar.Core.Tests.Fixtures.bundled-source.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);
        var checkedEvents = 0;

        foreach (var item in document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray())
        {
            var type = item.GetProperty("event").GetString() ?? string.Empty;
            if (!expectedByType.TryGetValue(type, out var expected)) continue;
            var stage = item.GetProperty("stage").GetString();

            Assert.Equal(expected, EventClassifier.Classify(type, stage));
            checkedEvents++;
        }

        Assert.True(checkedEvents >= 400, $"Ожидалось проверить большую часть встроенного снимка, проверено: {checkedEvents}.");
    }
}
