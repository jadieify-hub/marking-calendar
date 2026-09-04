using MarkingCalendar.Core.Changes;

namespace MarkingCalendar.App.Web;

public sealed record AppStatusViewModel(string Kind, string Message);
public sealed record ToastViewModel(string Kind, string Message, string? Action = null, string? BatchId = null);
public sealed record CategoryViewModel(string Id, string Label, string Color, string ColorDark);
public sealed record ProductGroupViewModel(
    string Key,
    string Name,
    int EventCount,
    string? FirstSeen = null,
    string? FirstEventDate = null,
    bool IsNew = false,
    string? RenamedFrom = null,
    bool IsCompleted = false,
    bool HasGoodsPage = true);
public sealed record GroupSuggestionViewModel(
    string Key,
    string Name,
    int EventCount,
    string? FirstEventDate,
    string Message);
public sealed record RoleViewModel(string Id, string Label);
public sealed record SectorViewModel(string Id, string Label, int ActiveGroupCount, IReadOnlyList<string> GroupKeys);
public sealed record UserProfileViewModel(
    IReadOnlyList<RoleViewModel> Roles,
    IReadOnlyList<SectorViewModel> Sectors,
    IReadOnlyList<string> SelectedRoles,
    IReadOnlyList<string> SelectedSectors,
    IReadOnlyDictionary<string, bool> ManualGroups,
    IReadOnlyList<string> RoleCategories,
    bool OnboardingCompleted);
public sealed record ArchiveViewModel(string Id, string RetrievedAt);
public sealed record ChangedFieldViewModel(string Field, string Previous, string Current);
public sealed record EventLineageEntryViewModel(
    string Kind,
    string CheckedAt,
    string? PreviousStart,
    string? PreviousEnd,
    string? PreviousStage,
    string? PreviousDescription,
    IReadOnlyList<ChangedFieldViewModel> ChangedFields);
public sealed record CalendarEventViewModel(
    string Id,
    string? Start,
    string? End,
    string Period,
    string Group,
    string Type,
    string TypeLabel,
    string Stage,
    string Description,
    string? Url,
    string Category,
    EventLineageEntryViewModel? RecentChange,
    int MoveCount,
    IReadOnlyList<EventLineageEntryViewModel> History);
public sealed record ChangeCountsViewModel(
    int Moved,
    int Added,
    int Changed,
    int Removed,
    int Total,
    int GroupsAdded = 0,
    int GroupsRenamed = 0);
public sealed record ChangeSummaryViewModel(
    string Kind,
    string Title,
    string Detail,
    string Stage,
    IReadOnlyList<ChangedFieldViewModel> ChangedFields,
    bool Mine);
public sealed record ChangeBatchViewModel(
    string Id,
    string CheckedAt,
    bool IsUnread,
    ChangeCountsViewModel Counts,
    int MineCount,
    int OthersCount,
    IReadOnlyList<ChangeSummaryViewModel> Items);
public sealed record ChangeHistoryViewModel(IReadOnlyList<ChangeBatchViewModel> Batches, int UnreadCount);
public sealed record UpdateNoticeViewModel(
    string BatchId,
    ChangeCountsViewModel Counts,
    int MineCount,
    int OthersCount,
    IReadOnlyList<ChangeSummaryViewModel> Items,
    IReadOnlyList<string>? RelatedBatchIds = null);
public sealed record ComparisonViewModel(
    string BaseRetrievedAt,
    ChangeCountsViewModel Counts,
    int MineCount,
    int OthersCount,
    IReadOnlyList<ChangeSummaryViewModel> Items);
public sealed record SnapshotComparison(DateTimeOffset BaseRetrievedAt, ChangeSummaryResult Summary);
public sealed record ProductViewModel(
    string Name,
    string Version,
    string Developer,
    string Publisher,
    string RepositoryUrl,
    string HistoryUrl,
    string SupportUrl,
    string Disclaimer,
    bool PublicHistoryEnabled = true,
    bool ChangeNotificationsEnabled = true);
public sealed record AppUpdateViewModel(
    string Kind,
    string Message,
    int? Progress,
    string? Version,
    bool CanRestart);
public sealed record AppViewModel(
    string UpdatedAt,
    int EventCount,
    string Today,
    IReadOnlyList<ProductGroupViewModel> Groups,
    IReadOnlyList<string> SelectedGroups,
    bool HasSelectedGroups,
    string Theme,
    IReadOnlyList<CategoryViewModel> Categories,
    IReadOnlyList<CalendarEventViewModel> Events,
    IReadOnlyList<ArchiveViewModel> Archives,
    ComparisonViewModel? Comparison,
    ChangeHistoryViewModel History,
    AppStatusViewModel Status,
    UpdateNoticeViewModel? UpdateNotice,
    ToastViewModel? Toast,
    AppUpdateViewModel AppUpdate,
    ProductViewModel About,
    IReadOnlyList<GroupSuggestionViewModel> GroupSuggestions,
    UserProfileViewModel Profile = null!);
