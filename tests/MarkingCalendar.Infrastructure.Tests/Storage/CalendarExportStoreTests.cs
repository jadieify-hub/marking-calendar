using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Storage;

public sealed class CalendarExportStoreTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task ResolveAsync_PreservesUidAfterMoveAndWordingEditAcrossReloads()
    {
        var original = Event("original", 1);
        var initial = await Store().ResolveAsync([original], CancellationToken.None);
        var moved = original with { Id = "moved", Start = new DateOnly(2026, 10, 5) };
        var afterMove = await Store().ResolveAsync([moved], CancellationToken.None);
        var edited = moved with { Id = "edited", Stage = "Уточнённый этап", Description = "Новая редакция" };
        var afterEdit = await Store().ResolveAsync([edited], CancellationToken.None);

        Assert.Equal(initial[original.Id], afterMove[moved.Id]);
        Assert.Equal(initial[original.Id], afterEdit[edited.Id]);
    }

    [Fact]
    public async Task ResolveAsync_KeepsSimilarEventsDistinctAndTransfersGroupRename()
    {
        var first = Event("first", 1);
        var second = Event("second", 20);
        var initial = await Store().ResolveAsync([first, second], CancellationToken.None);
        var renamedFirst = first with { Id = "renamed-first", Group = "Новое название" };
        var renamedSecond = second with { Id = "renamed-second", Group = "Новое название" };
        var renamed = await Store().ResolveAsync([renamedSecond, renamedFirst], CancellationToken.None);

        Assert.Equal(initial[first.Id], renamed[renamedFirst.Id]);
        Assert.Equal(initial[second.Id], renamed[renamedSecond.Id]);
        Assert.NotEqual(renamed[renamedFirst.Id], renamed[renamedSecond.Id]);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReuseMovedEventsUidForReintroducedOriginalContent()
    {
        var original = Event("original", 1);
        await Store().ResolveAsync([original], CancellationToken.None);
        var moved = original with { Id = "moved", Start = new DateOnly(2026, 10, 5) };
        var afterMove = await Store().ResolveAsync([moved], CancellationToken.None);
        var split = await Store().ResolveAsync([original, moved], CancellationToken.None);

        Assert.Equal(afterMove[moved.Id], split[moved.Id]);
        Assert.NotEqual(split[moved.Id], split[original.Id]);
        var reloaded = await Store().ResolveAsync([moved, original], CancellationToken.None);
        Assert.Equal(split[original.Id], reloaded[original.Id]);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotSilentlyResetCorruptIdentityFile()
    {
        var original = Event("original", 1);
        await Store().ResolveAsync([original], CancellationToken.None);
        await File.WriteAllTextAsync(_paths.ExportIdentityFile, "{broken");

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => Store().ResolveAsync([original], CancellationToken.None));
        Assert.Equal("{broken", await File.ReadAllTextAsync(_paths.ExportIdentityFile));
    }

    private CalendarExportStore Store() => new(_paths, new AtomicFileWriter());

    private static CalendarEvent Event(string id, int day) => new(
        id, new DateOnly(2026, 10, day), null, "с октября", "БАД", "Маркировка", "Старт", "Описание",
        new Uri("https://честныйзнак.рф/business/projects/bad/"));

    public void Dispose()
    {
        if (Directory.Exists(_paths.RootDirectory)) Directory.Delete(_paths.RootDirectory, recursive: true);
    }
}
