using System.IO;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Hosting;

public enum SnapshotOrigin
{
    Current,
    Archive,
    Bundled
}

public sealed record SnapshotRecoveryResult(CalendarSnapshot Snapshot, SnapshotOrigin Origin);

public sealed class SnapshotRecoveryService(
    CalendarStore store,
    Func<CancellationToken, Task<CalendarSnapshot>> loadBundled)
{
    private readonly CalendarStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Func<CancellationToken, Task<CalendarSnapshot>> _loadBundled =
        loadBundled ?? throw new ArgumentNullException(nameof(loadBundled));

    public async Task<SnapshotRecoveryResult> ResolveAsync(CancellationToken cancellationToken)
    {
        var current = await _store.LoadCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            return new SnapshotRecoveryResult(current, SnapshotOrigin.Current);
        }

        var archive = await _store.LoadLatestArchiveAsync(cancellationToken).ConfigureAwait(false);
        if (archive is not null)
        {
            await SaveRequiredAsync(archive, cancellationToken).ConfigureAwait(false);
            return new SnapshotRecoveryResult(archive, SnapshotOrigin.Archive);
        }

        var bundled = await _loadBundled(cancellationToken).ConfigureAwait(false);
        await SaveRequiredAsync(bundled, cancellationToken).ConfigureAwait(false);
        return new SnapshotRecoveryResult(bundled, SnapshotOrigin.Bundled);
    }

    private async Task SaveRequiredAsync(CalendarSnapshot snapshot, CancellationToken cancellationToken)
    {
        var saved = await _store.SaveValidatedAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (!saved.Saved)
        {
            throw new InvalidDataException("Резервный снимок календаря не прошёл проверку.");
        }
    }
}
