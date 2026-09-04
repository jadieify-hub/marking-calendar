using System.Text.Json;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed record AppState(
    int Version,
    IReadOnlyList<string> SeenBatchIds,
    IReadOnlyList<string> SelectedGroups,
    string Theme,
    bool PublicHistoryEnabled = true,
    DateTimeOffset? LastPublicHistorySync = null,
    IReadOnlyList<string> HiddenGroupSuggestions = null!,
    IReadOnlyList<string> Roles = null!,
    IReadOnlyList<string> SelectedSectors = null!,
    IReadOnlyDictionary<string, bool> ManualGroups = null!,
    bool OnboardingCompleted = false,
    bool ChangeNotificationsEnabled = true)
{
    private static readonly HashSet<string> KnownRoles = ["retail", "producer", "wholesale"];
    public static AppState Initial { get; } = new(6, [], [], "auto", true, null, [], [], [], new Dictionary<string, bool>(), false, true);

    public AppState WithSeen(IEnumerable<string> batchIds) => Normalize(this with
    {
        SeenBatchIds = SeenBatchIds.Concat(batchIds).ToArray()
    });

    public AppState WithGroups(IEnumerable<string> groups) => Normalize(this with
    {
        SelectedGroups = groups.ToArray()
    });

    public AppState WithTheme(string theme) => Normalize(this with { Theme = theme });

    public AppState WithProfile(
        IEnumerable<string> roles,
        IEnumerable<string> sectors,
        IReadOnlyDictionary<string, bool> manualGroups,
        IEnumerable<string> selectedGroups,
        bool completed = true) => Normalize(this with
        {
            Roles = roles.ToArray(),
            SelectedSectors = sectors.ToArray(),
            ManualGroups = manualGroups,
            SelectedGroups = selectedGroups.ToArray(),
            OnboardingCompleted = completed
        });

    public AppState WithGroupPreferences(
        IEnumerable<string> selectedGroups,
        IReadOnlyDictionary<string, bool> manualGroups) => Normalize(this with
        {
            SelectedGroups = selectedGroups.ToArray(),
            ManualGroups = manualGroups
        });

    public AppState CompleteOnboarding() => Normalize(this with { OnboardingCompleted = true });

    public AppState WithHiddenGroupSuggestions(IEnumerable<string> groups) => Normalize(this with
    {
        HiddenGroupSuggestions = groups.ToArray()
    });

    public AppState WithPublicHistory(bool enabled) => Normalize(this with
    {
        PublicHistoryEnabled = enabled
    });

    public AppState WithChangeNotifications(bool enabled) => Normalize(this with
    {
        ChangeNotificationsEnabled = enabled
    });

    public AppState WithPublicHistorySync(DateTimeOffset syncedAt, IEnumerable<string> seenBatchIds) => Normalize(this with
    {
        LastPublicHistorySync = syncedAt,
        SeenBatchIds = SeenBatchIds.Concat(seenBatchIds).ToArray()
    });

    public static AppState Normalize(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var seen = NormalizeValues(state.SeenBatchIds, StringComparer.Ordinal);
        var groups = NormalizeGroupKeys(state.SelectedGroups);
        var hiddenGroups = NormalizeGroupKeys(state.HiddenGroupSuggestions);
        var roles = NormalizeValues(state.Roles, StringComparer.Ordinal).Where(KnownRoles.Contains).ToArray();
        var sectors = NormalizeValues(state.SelectedSectors, StringComparer.Ordinal)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var manualGroups = NormalizeManualGroups(state.ManualGroups);
        var theme = state.Theme is "light" or "dark" ? state.Theme : "auto";
        var onboardingCompleted = state.OnboardingCompleted || (state.Version < 5 && groups.Length > 0);
        return new AppState(6, seen, groups, theme, state.PublicHistoryEnabled, state.LastPublicHistorySync, hiddenGroups, roles, sectors, manualGroups, onboardingCompleted, state.ChangeNotificationsEnabled);
    }

    private static string[] NormalizeValues(IEnumerable<string>? values, StringComparer comparer) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(comparer)
            .ToArray();

    private static string[] NormalizeGroupKeys(IEnumerable<string>? values) => (values ?? [])
        .Select(GroupKey.Normalize)
        .Where(value => value.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static SortedDictionary<string, bool> NormalizeManualGroups(IReadOnlyDictionary<string, bool>? values)
    {
        var result = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (rawKey, included) in values ?? new Dictionary<string, bool>())
        {
            var key = GroupKey.Normalize(rawKey);
            if (key.Length > 0) result[key] = included;
        }
        return result;
    }
}

public sealed class AppStateStore(AppPaths paths, IAtomicFileWriter writer)
{
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.StateFile)) return AppState.Initial;
        await using var stream = File.OpenRead(_paths.StateFile);
        var persisted = await JsonSerializer.DeserializeAsync<PersistedAppState>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
        if (persisted is null) return AppState.Initial;
        var seen = persisted.SeenBatchIds ?? (string.IsNullOrWhiteSpace(persisted.LastShownBatchId) ? [] : [persisted.LastShownBatchId]);
        return AppState.Normalize(new AppState(
            persisted.Version,
            seen,
            persisted.SelectedGroups ?? [],
            persisted.Theme ?? "auto",
            persisted.PublicHistoryEnabled ?? true,
            persisted.LastPublicHistorySync,
            persisted.HiddenGroupSuggestions ?? [],
            persisted.Roles ?? [],
            persisted.SelectedSectors ?? [],
            persisted.ManualGroups ?? new Dictionary<string, bool>(),
            persisted.OnboardingCompleted ?? false,
            persisted.ChangeNotificationsEnabled ?? true));
    }

    public Task SaveAsync(AppState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _writer.WriteJsonAsync(_paths.StateFile, AppState.Normalize(state), cancellationToken);
    }

    private sealed record PersistedAppState(
        int Version,
        string? LastShownBatchId,
        IReadOnlyList<string>? SeenBatchIds,
        IReadOnlyList<string>? SelectedGroups,
        string? Theme,
        bool? PublicHistoryEnabled,
        DateTimeOffset? LastPublicHistorySync,
        IReadOnlyList<string>? HiddenGroupSuggestions,
        IReadOnlyList<string>? Roles,
        IReadOnlyList<string>? SelectedSectors,
        IReadOnlyDictionary<string, bool>? ManualGroups,
        bool? OnboardingCompleted,
        bool? ChangeNotificationsEnabled);
}
