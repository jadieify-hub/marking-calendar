using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Runner;

public static class TelegramAnnouncementRenderer
{
    public const int CharacterLimit = 3500;
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static async Task<string> LoadAndRenderAsync(
        string dataDirectory,
        string batchId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        var paths = new AppPaths(dataDirectory, AppStorageLayout.Flat);
        using var store = new CalendarStore(
            paths,
            new SnapshotValidator(),
            new AtomicFileWriter(),
            maxHistoryBatches: 500);
        var history = await store.LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
        var batch = history.Batches.FirstOrDefault(item => item.Id.Equals(batchId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Пакет изменений {batchId} не найден.");
        return Render(batch);
    }

    public static string Render(ChangeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var checkedAt = batch.CheckedAt.ToOffset(MoscowOffset);
        var today = DateOnly.FromDateTime(checkedAt.DateTime);
        var factory = new ChangeSummaryFactory();
        var maximumItems = Math.Min(30, batch.Changes.Total);
        for (var itemLimit = maximumItems; itemLimit >= 0; itemLimit--)
        {
            var summary = factory.Create(batch.Changes, itemLimit, today, new HashSet<string>());
            var text = ChangeSummaryTextFormatter.Format(summary, checkedAt, new HashSet<string>());
            if (text.Length <= CharacterLimit) return text;
        }

        throw new InvalidOperationException("Не удалось сформировать сообщение Telegram допустимой длины.");
    }
}
