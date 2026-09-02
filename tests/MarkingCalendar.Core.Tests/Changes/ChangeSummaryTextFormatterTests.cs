using MarkingCalendar.Core.Changes;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class ChangeSummaryTextFormatterTests
{
    [Fact]
    public void Format_ProducesStableGroupedRussianText()
    {
        var result = new ChangeSummaryResult(
            new ChangeCounts(1, 0, 1, 0),
            [
                Item("Молочная продукция — Розничная продажа", "01.12.2026 → 01.06.2027", true),
                Item("Обувь — Маркировка", "01.10.2026", false)
            ],
            1,
            1);

        var text = ChangeSummaryTextFormatter.Format(
            result,
            new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.FromHours(3)),
            new HashSet<string>(["Молочная продукция"]));

        Assert.Equal(
            """
            Календарь маркировки — изменения от 02.09.2026 12:30
            Перенесено 1, добавлено 1, изменено 0, удалено 0
            По вашим группам (1):
            • Молочная продукция — Розничная продажа: 01.12.2026 → 01.06.2027.
              Старт передачи сведений
            По остальным группам (1):
            • Обувь — Маркировка: 01.10.2026.
              Старт передачи сведений
            Источник: честныйзнак.рф, проверено приложением «Календарь маркировки»
            """.ReplaceLineEndings("\n"),
            text);
    }

    [Fact]
    public void Format_UsesOneSectionWithoutSelectedGroups()
    {
        var result = new ChangeSummaryResult(new ChangeCounts(1, 0, 0, 0), [Item("Игрушки — Маркировка", "01.10.2026", false)], 0, 1);

        var text = ChangeSummaryTextFormatter.Format(result, new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.FromHours(3)), new HashSet<string>());

        Assert.Contains("Изменения (1):\n• Игрушки — Маркировка", text, StringComparison.Ordinal);
        Assert.DoesNotContain("По вашим группам", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LimitsOutputToThirtyEvents()
    {
        var items = Enumerable.Range(1, 35).Select(index => Item($"Группа {index} — Маркировка", "01.10.2026", false)).ToArray();
        var result = new ChangeSummaryResult(new ChangeCounts(35, 0, 0, 0), items, 0, 35);

        var text = ChangeSummaryTextFormatter.Format(result, new DateTimeOffset(2026, 9, 2, 12, 30, 0, TimeSpan.FromHours(3)), new HashSet<string>());

        Assert.Equal(30, text.Split('\n').Count(line => line.StartsWith("• ", StringComparison.Ordinal)));
        Assert.Contains("и ещё 5", text, StringComparison.Ordinal);
    }

    private static ChangeSummary Item(string title, string detail, bool mine) =>
        new(ChangeKind.Added, title, detail, "Старт передачи сведений", new DateOnly(2026, 10, 1), [], mine);
}
