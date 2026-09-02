using System.Text.Json;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;
using MarkingCalendar.Runner;

namespace MarkingCalendar.Runner.Tests;

public sealed class GroupMapCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsGroupsMissingOnEachSideWithoutFailing()
    {
        using var temp = new TemporaryDirectory();
        await WriteAsync(Path.Combine(temp.Path, "groups.json"), new GroupMap(
            2,
            "2026-09-02",
            [new("home", "Для дома")],
            [new("Игрушки", "/business/projects/toys/", ["home"]),
             new("Старая группа", "/business/projects/old/", ["home"], "completed")]));
        await WriteAsync(Path.Combine(temp.Path, "current.json"), Snapshot(
            Event("Игрушки", "/business/projects/toys/"),
            Event("Новая группа", "/business/projects/new/")));

        var result = await GroupMapCommandHandler.ExecuteAsync(temp.Path, CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Success, result.ExitCode);
        Assert.Contains("Не размечены в карте: Новая группа", result.Output, StringComparison.Ordinal);
        Assert.Contains("Отсутствуют в снимке: Старая группа", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidMap()
    {
        using var temp = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "groups.json"), """
            {"schemaVersion":2,"updatedAt":"2026-09-02","sectors":[{"id":"home","label":"Дом"}],"groups":[{"name":"A","link":"/a/","sectors":["missing"]}]}
            """);
        await WriteAsync(Path.Combine(temp.Path, "current.json"), Snapshot(Event("A", "/a/")));

        var result = await GroupMapCommandHandler.ExecuteAsync(temp.Path, CancellationToken.None);

        Assert.Equal(HistoryRunnerExitCode.Rejected, result.ExitCode);
        Assert.Contains("неизвестную отрасль missing", result.Output, StringComparison.Ordinal);
    }

    private static CalendarSnapshot Snapshot(params CalendarEvent[] events) =>
        CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero), new Uri("https://example.test"), events);

    private static CalendarEvent Event(string group, string path) => new(
        EventId.FromCanonicalContent(group),
        new DateOnly(2026, 9, 2),
        null,
        "02.09.2026",
        group,
        "Регистрация",
        "Старт",
        "Описание",
        new Uri("https://честныйзнак.рф" + path));

    private static async Task WriteAsync<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonDefaults.Options));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkingCalendar.Runner.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
