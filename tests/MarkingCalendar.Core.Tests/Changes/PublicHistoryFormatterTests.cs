using System.Text;
using System.Xml;
using System.Xml.Linq;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Changes;

public sealed class PublicHistoryFormatterTests
{
    [Fact]
    public void Markdown_ProducesStableRussianHistoryForThreeBatchesIncludingEndOnlyMove()
    {
        var previous = IntervalEvent("old", new DateOnly(2025, 12, 1), new DateOnly(2026, 8, 31));
        var current = IntervalEvent("new", new DateOnly(2025, 12, 1), new DateOnly(2027, 2, 28));
        var history = new ChangeHistory([
            Batch("batch-3", new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero), new ChangeSet([], [], [EventChange.Moved(previous, current)], [])),
            Batch("batch-2", new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), new ChangeSet([Event("added", "Обувь", "Старт", new DateOnly(2026, 10, 1))], [], [], [])),
            Batch("batch-1", new DateTimeOffset(2026, 8, 31, 6, 0, 0, TimeSpan.Zero), new ChangeSet([], [Event("removed", "Игрушки", "Продажа", new DateOnly(2026, 11, 1))], [], []))
        ]);

        var markdown = ChangeMarkdownFormatter.Format(history);

        Assert.Equal(
            """
            # История изменений календаря маркировки

            Обновляется автоматически, источник честныйзнак.рф, приложение не является официальным продуктом оператора.

            ## 02.09.2026, 09:00 МСК — 1 изменение

            **Перенесено (1)**

            - Детские игрушки — Розничная продажа: Окончание: 31.08.2026 → 28.02.2027. Маркировка остатков
              - период — было: 01.12.2025–31.08.2026
              - период — стало: 01.12.2025–28.02.2027

            ## 01.09.2026, 09:00 МСК — 1 изменение

            **Добавлено (1)**

            - Обувь — Розничная продажа: 01.10.2026. Старт

            ## 31.08.2026, 09:00 МСК — 1 изменение

            **Удалено (1)**

            - Игрушки — Розничная продажа: 01.11.2026. Продажа
            """.ReplaceLineEndings("\n"),
            markdown);
    }

    [Fact]
    public void Atom_IsValidXmlAndLimitsFeedToFiftyNewestBatches()
    {
        var batches = Enumerable.Range(1, 55)
            .Select(index => Batch(
                $"batch-{index:00}",
                new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero).AddMinutes(index),
                index == 55
                    ? new ChangeSet([], [], [EventChange.Moved(
                        IntervalEvent("old", new DateOnly(2025, 12, 1), new DateOnly(2026, 8, 31)),
                        IntervalEvent("new", new DateOnly(2025, 12, 1), new DateOnly(2027, 2, 28)))], [])
                    : new ChangeSet([Event($"event-{index}", $"Группа {index}", "Старт", new DateOnly(2026, 10, 1))], [], [], [])))
            .ToArray();

        var xml = AtomFeedWriter.Write(
            new ChangeHistory(batches),
            new Uri("https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md"),
            new DateOnly(2026, 9, 2));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var reader = XmlReader.Create(stream);
        var document = XDocument.Load(reader);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entries = document.Root?.Elements(atom + "entry").ToArray() ?? [];

        Assert.Equal(50, entries.Length);
        Assert.Equal("batch-55", entries[0].Element(atom + "id")?.Value);
        Assert.Equal(TimeSpan.FromHours(3), DateTimeOffset.Parse(entries[0].Element(atom + "updated")!.Value, System.Globalization.CultureInfo.InvariantCulture).Offset);
        Assert.Equal("text", entries[0].Element(atom + "content")?.Attribute("type")?.Value);
        Assert.Equal(
            """
            Календарь маркировки — изменения от 02.09.2026 09:55
            Перенесено 1, добавлено 0, изменено 0, удалено 0
            Изменения (1):
            • Детские игрушки — Розничная продажа: Окончание: 31.08.2026 → 28.02.2027, формулировка изменена.
              Маркировка остатков
            Источник: честныйзнак.рф, проверено приложением «Календарь маркировки»
            """.ReplaceLineEndings("\n"),
            entries[0].Element(atom + "content")?.Value);
    }

    [Fact]
    public void Markdown_LimitsDocumentToTwoHundredBatchesAndLinksFullJson()
    {
        var history = new ChangeHistory(Enumerable.Range(1, 205)
            .Select(index => Batch($"batch-{index}", new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero).AddMinutes(index), ChangeSet.Empty))
            .ToArray());

        var markdown = ChangeMarkdownFormatter.Format(history);

        Assert.Equal(200, markdown.Split("\n## ", StringSplitOptions.None).Length - 1);
        Assert.Contains("[history/changes.json](history/changes.json)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_EscapesSourceControlledMarkup()
    {
        var item = Event("unsafe", "<script>*группа*</script>", "[этап]", new DateOnly(2026, 10, 1));

        var markdown = ChangeMarkdownFormatter.Format(new ChangeHistory([
            Batch("batch", new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero), new ChangeSet([item], [], [], []))
        ]));

        Assert.DoesNotContain("<script>", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;\\*группа\\*&lt;/script&gt;", markdown, StringComparison.Ordinal);
        Assert.Contains("\\[этап\\]", markdown, StringComparison.Ordinal);
    }

    private static ChangeBatch Batch(string id, DateTimeOffset checkedAt, ChangeSet changes) =>
        new(id, checkedAt, changes, "previous", "current", ChangeBatchSources.Public);

    private static CalendarEvent Event(string id, string group, string stage, DateOnly date) =>
        new(id, date, null, $"с {date:dd.MM.yyyy}", group, "Розничная продажа", stage, "Описание", null);

    private static CalendarEvent IntervalEvent(string id, DateOnly start, DateOnly end) =>
        new(
            id,
            start,
            end,
            $"{start:dd.MM.yyyy}–{end:dd.MM.yyyy}",
            "Детские игрушки",
            "Розничная продажа",
            "Маркировка остатков",
            "Описание",
            null);
}
