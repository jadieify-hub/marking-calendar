using System.Text;
using System.Reflection;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Migration;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Migration;

public sealed class LegacyCalendarImporterTests
{
    [Fact]
    public async Task ImportOnceAsync_ImportsValidCalendarWithoutChangingLegacyFiles()
    {
        using var temp = new TemporaryDirectories();
        var calendarPath = Path.Combine(temp.Legacy, "calendar-data.js");
        var historyPath = Path.Combine(temp.Legacy, "change-history.json");
        await File.WriteAllTextAsync(calendarPath, ValidCalendar, Encoding.UTF8);
        await File.WriteAllTextAsync(historyPath, ReadFixture("legacy-change-history.json"), Encoding.UTF8);
        var calendarBytes = await File.ReadAllBytesAsync(calendarPath);
        var historyBytes = await File.ReadAllBytesAsync(historyPath);
        using var store = new CalendarStore(new AppPaths(temp.Current), new SnapshotValidator(), new AtomicFileWriter());
        var importer = new LegacyCalendarImporter(temp.Legacy, new AppPaths(temp.Current), store, new EventNormalizer());

        var first = await importer.ImportOnceAsync(CancellationToken.None);
        var second = await importer.ImportOnceAsync(CancellationToken.None);
        var snapshot = await store.LoadCurrentAsync(CancellationToken.None);
        var history = await store.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(LegacyImportStatus.Imported, first.Status);
        Assert.Equal(LegacyImportStatus.AlreadyImported, second.Status);
        Assert.Single(Assert.IsType<CalendarSnapshot>(snapshot).Events);
        var batch = Assert.Single(history.Batches);
        Assert.Equal(2, batch.Changes.Total);
        Assert.Equal(ChangeBatchIdFactory.FromChanges(batch.CheckedAt, batch.Changes), batch.Id);
        Assert.Equal(calendarBytes, await File.ReadAllBytesAsync(calendarPath));
        Assert.Equal(historyBytes, await File.ReadAllBytesAsync(historyPath));
    }

    [Fact]
    public async Task ImportOnceAsync_ImportsValidHistoryBatchesAndSkipsRejectedBatch()
    {
        using var temp = new TemporaryDirectories();
        await File.WriteAllTextAsync(Path.Combine(temp.Legacy, "calendar-data.js"), ValidCalendar, Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Legacy, "change-history.json"),
            ReadFixture("legacy-change-history-partial.json"),
            Encoding.UTF8);
        var paths = new AppPaths(temp.Current);
        using var store = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter());
        var logger = new RecordingLogger();
        var importer = new LegacyCalendarImporter(temp.Legacy, paths, store, new EventNormalizer(), logger);

        var result = await importer.ImportOnceAsync(CancellationToken.None);
        var history = await store.LoadHistoryAsync(CancellationToken.None);

        Assert.Equal(LegacyImportStatus.Imported, result.Status);
        Assert.Single(history.Batches);
        Assert.Contains(logger.Entries, entry => entry.Level == AppLogLevel.Warning && entry.Source == "legacy-import");
        Assert.Single(logger.RejectedPayloads);
    }

    [Fact]
    public async Task ImportOnceAsync_RejectsInvalidLegacyPayloadWithoutMarker()
    {
        using var temp = new TemporaryDirectories();
        await File.WriteAllTextAsync(Path.Combine(temp.Legacy, "calendar-data.js"), "window.CHZ_CALENDAR_DATA = {\"events\":[]};", Encoding.UTF8);
        var paths = new AppPaths(temp.Current);
        using var store = new CalendarStore(paths, new SnapshotValidator(), new AtomicFileWriter());
        var logger = new RecordingLogger();
        var importer = new LegacyCalendarImporter(temp.Legacy, paths, store, new EventNormalizer(), logger);

        var result = await importer.ImportOnceAsync(CancellationToken.None);

        Assert.Equal(LegacyImportStatus.Rejected, result.Status);
        Assert.False(File.Exists(paths.MigrationMarker));
        Assert.Null(await store.LoadCurrentAsync(CancellationToken.None));
        Assert.Contains(logger.Entries, entry => entry.Level == AppLogLevel.Warning && entry.Source == "legacy-import");
        Assert.Contains("window.CHZ_CALENDAR_DATA", Assert.Single(logger.RejectedPayloads).Json, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectories : IDisposable
    {
        public TemporaryDirectories()
        {
            Root = Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
            Legacy = Path.Combine(Root, "legacy");
            Current = Path.Combine(Root, "current");
            Directory.CreateDirectory(Legacy);
            Directory.CreateDirectory(Current);
        }

        public string Root { get; }
        public string Legacy { get; }
        public string Current { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static string ReadFixture(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"MarkingCalendar.Infrastructure.Tests.Fixtures.{name}");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private const string ValidCalendar = """
        window.CHZ_CALENDAR_DATA = {
          "updatedAt":"2026-09-01T10:45:00+03:00",
          "sourceUrl":"https://честныйзнак.рф/source",
          "events":[{
            "start":"2026-09-01","end":"","period":"с 1 сентября 2026",
            "group":"Игрушки","type":"Розничная продажа","stage":"Старт",
            "description":"Описание","url":"https://честныйзнак.рф/business/projects/children/"
          }]
        };
        """;

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(AppLogLevel Level, string Source)> Entries { get; } = [];
        public List<(string Source, string Json)> RejectedPayloads { get; } = [];

        public void Log(AppLogLevel level, string source, string message, Exception? exception = null) =>
            Entries.Add((level, source));

        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken)
        {
            RejectedPayloads.Add((source, json));
            return Task.CompletedTask;
        }
    }
}
