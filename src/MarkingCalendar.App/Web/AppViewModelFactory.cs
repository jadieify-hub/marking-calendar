using System.Globalization;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.App.Updates;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Web;

public sealed class AppViewModelFactory(IChangeSummaryFactory summaryFactory, TimeProvider timeProvider)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly IReadOnlyList<CategoryViewModel> Categories =
    [
        new("retail", "Розничная продажа", "#1f93bb", "#3fbde4"),
        new("edo", "ЭДО и учёт", "#7b4fd0", "#a583f0"),
        new("ban", "Запрет оборота", "#cf4842", "#ec7069"),
        new("permit", "Разрешительный режим", "#b8801d", "#e0aa4a"),
        new("marking", "Маркировка", "#1e9a63", "#3fc98a"),
        new("registration", "Регистрация", "#3d72bd", "#6ea3ef"),
        new("other", "Прочее", "#6b7783", "#8e9aa7")
    ];
    private static readonly IReadOnlyList<RoleViewModel> Roles =
    [
        new("retail", "Розница"),
        new("producer", "Производство или импорт"),
        new("wholesale", "Опт")
    ];

    private readonly IChangeSummaryFactory _summaryFactory = summaryFactory ?? throw new ArgumentNullException(nameof(summaryFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public AppViewModel Create(
        CalendarSnapshot snapshot,
        ChangeHistory history,
        AppStatusViewModel status,
        ChangeBatch? noticeBatch,
        AppState? state,
        AppUpdateState? appUpdateState = null,
        ToastViewModel? toast = null,
        IReadOnlyList<SnapshotArchiveInfo>? archives = null,
        SnapshotComparison? comparison = null,
        IReadOnlyList<string>? noticeRelatedBatchIds = null,
        GroupMap? groupMap = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(status);

        state = AppState.Normalize(state ?? AppState.Initial);
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var lineages = EventLineageBuilder.Build(history, snapshot.Events);
        var events = snapshot.Events.Select(item => Event(item, lineages[item.Id], today)).ToArray();
        var novelty = GroupNoveltyBuilder.Build(history);
        var newSince = today.AddDays(-60);
        var mappedGroups = groupMap is null
            ? new Dictionary<string, GroupMapEntry>(StringComparer.Ordinal)
            : GroupMapMatcher.Match(groupMap, snapshot.Events).Matches.ToDictionary(
                item => GroupKey.Normalize(item.SnapshotGroup),
                item => item.Entry,
                StringComparer.Ordinal);
        var groups = snapshot.Events
            .GroupBy(item => GroupKey.Normalize(item.Group), StringComparer.Ordinal)
            .Select(group =>
            {
                novelty.TryGetValue(group.Key, out var groupNovelty);
                mappedGroups.TryGetValue(group.Key, out var mappedGroup);
                var firstSeen = groupNovelty?.FirstSeen is { } firstSeenAt ? LocalDate(firstSeenAt) : (DateOnly?)null;
                var renamedRecently = groupNovelty?.RenamedAt is { } renamedAt && LocalDate(renamedAt) >= newSince;
                var firstEventDate = group.Select(item => item.Start ?? item.End).Where(date => date is not null).Min();
                return new ProductGroupViewModel(
                    group.Key,
                    group.First().Group.Trim(),
                    group.Count(),
                    Iso(firstSeen),
                    Iso(firstEventDate),
                    firstSeen is not null && firstSeen >= newSince,
                    renamedRecently ? groupNovelty?.RenamedFrom : null,
                    mappedGroup?.IsCompleted ?? false,
                    mappedGroup?.GoodsPath is not false);
            })
            .OrderBy(group => group.IsCompleted)
            .ThenBy(group => group.Name, StringComparer.Create(Russian, ignoreCase: true))
            .ToArray();
        var knownGroups = new HashSet<string>(groups.Select(group => group.Key), StringComparer.Ordinal);
        var storedGroupKeys = state.SelectedGroups.Select(GroupKey.Normalize).Distinct(StringComparer.Ordinal).ToArray();
        var selectedGroups = storedGroupKeys.Where(knownGroups.Contains).ToArray();
        var summaryGroups = new HashSet<string>(storedGroupKeys, StringComparer.Ordinal);
        var hiddenSuggestions = (state.HiddenGroupSuggestions ?? []).Select(GroupKey.Normalize).ToHashSet(StringComparer.Ordinal);
        var selectedSectors = state.SelectedSectors.ToHashSet(StringComparer.Ordinal);
        var suggestions = groups
            .Where(group => group.IsNew && !group.IsCompleted && !summaryGroups.Contains(group.Key) && !hiddenSuggestions.Contains(group.Key))
            .Select(group =>
            {
                mappedGroups.TryGetValue(group.Key, out var mappedGroup);
                var message = selectedSectors.Count == 0 || mappedGroup is null
                    ? "Новая группа в календаре"
                    : mappedGroup.Sectors.Any(selectedSectors.Contains)
                        ? "Новая группа в вашей отрасли"
                        : null;
                return message is null
                    ? null
                    : new GroupSuggestionViewModel(
                        group.Key,
                        group.Name,
                        group.EventCount,
                        group.FirstEventDate,
                        message);
            })
            .OfType<GroupSuggestionViewModel>()
            .ToArray();
        var profile = Profile(state, groupMap);
        var priorityCategories = UserRoleCategories.For(state.Roles);
        var seen = new HashSet<string>(state.SeenBatchIds, StringComparer.Ordinal);
        var historyBatches = history.Batches.Select(batch => Batch(batch, !seen.Contains(batch.Id), summaryGroups, priorityCategories)).ToArray();
        var historyView = new ChangeHistoryViewModel(historyBatches, historyBatches.Count(batch => batch.IsUnread));
        var notice = noticeBatch is not null
            ? Notice(noticeBatch, summaryGroups, noticeRelatedBatchIds, priorityCategories)
            : null;

        return new AppViewModel(
            snapshot.RetrievedAt.ToString("dd.MM.yyyy, HH:mm", Russian),
            events.Length,
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            groups,
            selectedGroups,
            state.SelectedGroups.Count > 0,
            state.Theme,
            Categories,
            events,
            (archives ?? []).Select(Archive).ToArray(),
            comparison is null ? null : Comparison(comparison),
            historyView,
            status,
            notice,
            toast,
            ApplicationUpdate(appUpdateState ?? AppUpdateState.Initial),
            new ProductViewModel(
                ProductInfo.Name,
                ProductInfo.Version,
                ProductInfo.Developer,
                ProductInfo.Publisher,
                ProductInfo.RepositoryUrl,
                ProductInfo.PublicHistoryUrl,
                ProductInfo.SupportUrl,
                ProductInfo.Disclaimer,
                state.PublicHistoryEnabled,
                state.ChangeNotificationsEnabled),
            suggestions,
            profile);
    }

    private static UserProfileViewModel Profile(AppState state, GroupMap? groupMap)
    {
        var roleCategories = UserRoleCategories.For(state.Roles);
        var sectors = groupMap?.Sectors.Select(sector => new SectorViewModel(
            sector.Id,
            sector.Label,
            groupMap.Groups.Count(group => !group.IsCompleted && group.Sectors.Contains(sector.Id, StringComparer.Ordinal)),
            groupMap.Groups
                .Where(group => !group.IsCompleted && group.Sectors.Contains(sector.Id, StringComparer.Ordinal))
                .Select(group => GroupKey.Normalize(group.Name))
                .ToArray())).ToArray() ?? [];
        return new UserProfileViewModel(
            Roles,
            sectors,
            state.Roles,
            state.SelectedSectors,
            state.ManualGroups,
            Categories.Select(category => category.Id)
                .Where(id => roleCategories.Contains(Category(id)))
                .ToArray(),
            state.OnboardingCompleted);
    }

    private ArchiveViewModel Archive(SnapshotArchiveInfo archive) =>
        new(archive.Id, LocalTime(archive.RetrievedAt).ToString("dd.MM.yyyy, HH:mm", Russian));

    private ComparisonViewModel Comparison(SnapshotComparison comparison) => new(
        LocalTime(comparison.BaseRetrievedAt).ToString("dd.MM.yyyy, HH:mm", Russian),
        Counts(comparison.Summary),
        comparison.Summary.MineCount,
        comparison.Summary.OthersCount,
        comparison.Summary.Items.Select(Item).ToArray());

    private static AppUpdateViewModel ApplicationUpdate(AppUpdateState state) => new(
        state.Stage switch
        {
            AppUpdateStage.Idle => "idle",
            AppUpdateStage.Checking => "checking",
            AppUpdateStage.NoUpdate => "current",
            AppUpdateStage.Downloading => "downloading",
            AppUpdateStage.ReadyToRestart => "ready",
            AppUpdateStage.Failed => "error",
            AppUpdateStage.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state.Stage, "Неизвестное состояние обновления приложения.")
        },
        state.Message,
        state.Progress,
        state.Version,
        state.Stage == AppUpdateStage.ReadyToRestart);

    private CalendarEventViewModel Event(CalendarEvent item, EventLineage lineage, DateOnly today)
    {
        var history = lineage.Entries.Select(LineageEntry).ToArray();
        var latest = lineage.Entries.Count > 0 ? lineage.Entries[0] : null;
        var latestDate = latest is null ? (DateOnly?)null : LocalDate(latest.CheckedAt);
        var recent = latest is not null
            && latestDate >= today.AddDays(-ChangeTrackingPolicy.RecentChangeWindowDays)
            && latestDate <= today
                ? LineageEntry(latest)
                : null;
        return new CalendarEventViewModel(
            item.Id,
            Iso(item.Start),
            Iso(item.End),
            item.Period,
            item.Group,
            item.Type,
            EventClassifier.TypeLabel(item.Type),
            item.Stage,
            item.Description,
            item.Url?.AbsoluteUri,
            CategoryId(EventClassifier.Classify(item.Type, item.Stage)),
            recent,
            lineage.MoveCount,
            history);
    }

    private static EventLineageEntryViewModel LineageEntry(EventLineageEntry entry) => new(
        Kind(entry.Kind),
        entry.CheckedAt.ToString("O", CultureInfo.InvariantCulture),
        Iso(entry.PreviousStart),
        Iso(entry.PreviousEnd),
        entry.PreviousStage,
        entry.PreviousDescription,
        entry.ChangedFields.Select(Field).ToArray());

    private DateOnly LocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(LocalTime(value).DateTime);

    private DateTimeOffset LocalTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _timeProvider.LocalTimeZone);

    private ChangeBatchViewModel Batch(
        ChangeBatch batch,
        bool isUnread,
        IReadOnlySet<string> selectedGroups,
        IReadOnlySet<EventCategory> priorityCategories)
    {
        var summary = _summaryFactory.Create(batch.Changes, int.MaxValue, DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime), selectedGroups, priorityCategories);
        return new ChangeBatchViewModel(
            batch.Id,
            batch.CheckedAt.ToString("dd.MM.yyyy, HH:mm", Russian),
            isUnread,
            Counts(summary, batch.Changes),
            summary.MineCount,
            summary.OthersCount,
            summary.Items.Select(Item).ToArray());
    }

    private UpdateNoticeViewModel Notice(
        ChangeBatch batch,
        IReadOnlySet<string> selectedGroups,
        IReadOnlyList<string>? relatedBatchIds,
        IReadOnlySet<EventCategory> priorityCategories)
    {
        var summary = _summaryFactory.Create(batch.Changes, 8, DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime), selectedGroups, priorityCategories);
        return new UpdateNoticeViewModel(
            batch.Id,
            Counts(summary, batch.Changes),
            summary.MineCount,
            summary.OthersCount,
            summary.Items.Select(Item).ToArray(),
            relatedBatchIds ?? [batch.Id]);
    }

    private static ChangeCountsViewModel Counts(ChangeSummaryResult summary, ChangeSet? changes = null) => new(
        summary.Counts.Moved,
        summary.Counts.Added,
        summary.Counts.Changed,
        summary.Counts.Removed,
        summary.Counts.Total,
        changes?.GroupsAdded.Count ?? 0,
        changes?.GroupsRenamed.Count ?? 0);

    private static ChangeSummaryViewModel Item(ChangeSummary item) =>
        new(Kind(item.Kind), item.Title, item.Detail, item.Stage, item.ChangedFields.Select(Field).ToArray(), item.Mine);

    private static ChangedFieldViewModel Field(ChangedField field) =>
        new(field.Field, field.Previous, field.Current);

    private static string Kind(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Removed => "removed",
        ChangeKind.Moved => "moved",
        ChangeKind.Changed => "changed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный тип изменения.")
    };

    private static string CategoryId(EventCategory category) => category switch
    {
        EventCategory.Retail => "retail",
        EventCategory.Edo => "edo",
        EventCategory.Ban => "ban",
        EventCategory.Permit => "permit",
        EventCategory.Marking => "marking",
        EventCategory.Registration => "registration",
        EventCategory.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Неизвестная категория события.")
    };

    private static EventCategory Category(string id) => id switch
    {
        "retail" => EventCategory.Retail,
        "edo" => EventCategory.Edo,
        "ban" => EventCategory.Ban,
        "permit" => EventCategory.Permit,
        "marking" => EventCategory.Marking,
        "registration" => EventCategory.Registration,
        "other" => EventCategory.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Неизвестный идентификатор категории.")
    };

    private static string? Iso(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
