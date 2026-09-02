using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Source;

public static class PublicHistorySyncPolicy
{
    public static bool ShouldSync(AppState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.PublicHistoryEnabled
            && (state.LastPublicHistorySync is null || now - state.LastPublicHistorySync.Value >= TimeSpan.FromDays(1));
    }

    public static AppState Apply(
        AppState state,
        ChangeHistory publicHistory,
        DateTimeOffset localSnapshotRetrievedAt,
        DateTimeOffset syncedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(publicHistory);
        var firstSync = state.LastPublicHistorySync is null;
        var seen = publicHistory.Batches
            .Where(batch => batch.Source.Equals(ChangeBatchSources.Public, StringComparison.Ordinal)
                && (firstSync || batch.CheckedAt <= localSnapshotRetrievedAt))
            .Select(batch => batch.Id);
        return state.WithPublicHistorySync(syncedAt, seen);
    }
}
