using MarkingCalendar.Core.Changes;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Web;

public sealed record UpdatePresentation(ChangeBatch? Notice, ToastViewModel? Toast, bool MarkSeen);

public sealed class UpdatePresentationPolicy(IChangeSummaryFactory summaryFactory, TimeProvider timeProvider)
{
    private readonly IChangeSummaryFactory _summaryFactory = summaryFactory ?? throw new ArgumentNullException(nameof(summaryFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public UpdatePresentation Evaluate(ChangeBatch batch, AppState state)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(state);
        if (state.SelectedGroups.Count == 0)
        {
            return new UpdatePresentation(batch, null, false);
        }

        if (batch.Changes.GroupTotal > 0)
        {
            return new UpdatePresentation(batch, null, false);
        }

        var selectedGroups = new HashSet<string>(state.SelectedGroups, StringComparer.OrdinalIgnoreCase);
        var summary = _summaryFactory.Create(
            batch.Changes,
            0,
            DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime),
            selectedGroups);
        if (summary.MineCount > 0)
        {
            return new UpdatePresentation(batch, null, false);
        }

        var message = $"Обновлено: {Plural(summary.OthersCount, "изменение", "изменения", "изменений")} по другим группам";
        return new UpdatePresentation(
            null,
            new ToastViewModel("success", message, "openChanges", batch.Id),
            true);
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var lastTwo = count % 100;
        var last = count % 10;
        var noun = lastTwo is >= 11 and <= 14 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many;
        return $"{count} {noun}";
    }
}
