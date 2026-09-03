using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Globalization;
using MarkingCalendar.App.Web;
using MarkingCalendar.App.Updates;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Migration;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;
using MarkingCalendar.Infrastructure.Updates;

namespace MarkingCalendar.App.Hosting;

public sealed class AppBootstrapper(MainWindow window, IAppLogger logger) : IDisposable
{
    private readonly MainWindow _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly HttpClient _httpClient = new();
    private CalendarStore? _store;
    private AppStateStore? _stateStore;
    private GroupMapStore? _groupMapStore;
    private CalendarUpdateService? _updateService;
    private AppUpdateService? _appUpdateService;
    private AppViewModelFactory? _viewModelFactory;
    private UpdatePresentationPolicy? _updatePresentationPolicy;
    private ChangeSummaryFactory? _summaryFactory;
    private EventDiffEngine? _diffEngine;
    private CalendarSnapshot? _snapshot;
    private CalendarSnapshot? _bundledSnapshot;
    private IReadOnlyList<SnapshotArchiveInfo> _archives = [];
    private SnapshotComparison? _comparison;
    private WpfClipboardService? _clipboardService;
    private ChangeHistory _history = ChangeHistory.Empty;
    private ChangeHistory _publicHistory = ChangeHistory.Empty;
    private PublicHistoryClient? _publicHistoryClient;
    private GroupMap? _groupMap;
    private AppState _state = AppState.Initial;
    private AppStatusViewModel _status = new("ready", "Сохранённые данные");
    private ChangeBatch? _notice;
    private IReadOnlyList<string> _noticeRelatedBatchIds = [];
    private ToastViewModel? _toast;
    private AppStatusViewModel? _fallbackStatus;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var paths = AppPaths.ForCurrentUser();
        var writer = new AtomicFileWriter();
        var normalizer = new EventNormalizer();
        _store = new CalendarStore(
            paths,
            new SnapshotValidator(),
            writer,
            timeProvider: TimeProvider.System,
            logger: _logger,
            maxHistoryBatches: 500);
        _stateStore = new AppStateStore(paths, writer);
        _groupMapStore = new GroupMapStore(paths, writer);
        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CHZ-MarkingCalendar");
        var importer = new LegacyCalendarImporter(legacyDirectory, paths, _store, normalizer, _logger);
        await importer.ImportOnceAsync(cancellationToken);

        _bundledSnapshot = await LoadBundledAsync(normalizer, cancellationToken);
        var bundledGroups = await LoadBundledGroupsAsync(cancellationToken);
        try
        {
            _groupMap = await _groupMapStore.LoadAsync(cancellationToken) ?? bundledGroups;
        }
        catch (InvalidDataException error)
        {
            _logger.Log(AppLogLevel.Warning, "groups", "Сохранённая карта групп повреждена, используется встроенная.", error);
            _groupMap = bundledGroups;
        }
        var recovery = await new SnapshotRecoveryService(
            _store,
            _ => Task.FromResult(_bundledSnapshot ?? throw new InvalidOperationException("Встроенный снимок не загружен."))).ResolveAsync(cancellationToken);
        _snapshot = recovery.Snapshot;
        LogGroupMapConflicts();
        _fallbackStatus = recovery.Origin switch
        {
            SnapshotOrigin.Archive => new AppStatusViewModel(
                "ready",
                $"Открыта резервная копия от {recovery.Snapshot.RetrievedAt.ToString("dd.MM.yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"))}"),
            SnapshotOrigin.Bundled => new AppStatusViewModel("ready", "Открыта встроенная версия"),
            _ => null
        };
        if (_fallbackStatus is not null) _status = _fallbackStatus;

        _state = await _stateStore.LoadAsync(cancellationToken);
        _window.ApplyTitleBarTheme(_state.Theme);
        _history = await _store.LoadHistoryAsync(cancellationToken);
        if (_history.Batches.Count == 0)
        {
            var bundledHistory = await LoadBundledHistoryAsync(cancellationToken);
            if (bundledHistory.Batches.Count > 0)
            {
                _history = bundledHistory;
                await _store.SaveHistoryAsync(_history, cancellationToken);
                _state = _state.WithSeen(_history.Batches.Select(batch => batch.Id));
                await _stateStore.SaveAsync(_state, cancellationToken);
            }
        }
        await ApplyGroupRenamesAsync(cancellationToken);
        _publicHistory = new ChangeHistory(_history.Batches
            .Where(batch => batch.Source.Equals(ChangeBatchSources.Public, StringComparison.Ordinal))
            .ToArray());
        _publicHistoryClient = new PublicHistoryClient(
            _httpClient,
            new Uri(ProductInfo.PublicHistoryManifestUrl),
            ProductInfo.Version);
        _archives = await BuildArchiveListAsync(cancellationToken);
        var summaryFactory = new ChangeSummaryFactory();
        var diffEngine = new EventDiffEngine();
        _summaryFactory = summaryFactory;
        _diffEngine = diffEngine;
        _viewModelFactory = new AppViewModelFactory(summaryFactory, TimeProvider.System);
        _updatePresentationPolicy = new UpdatePresentationPolicy(summaryFactory, TimeProvider.System);
        _updateService = new CalendarUpdateService(
            new MarkingCalendarClient(_httpClient, normalizer, TimeProvider.System, ProductInfo.Version),
            _store,
            diffEngine,
            TimeProvider.System,
            _logger);
        _appUpdateService = new AppUpdateService(new VelopackUpdateSource(), _logger);
        _appUpdateService.StateChanged += AppUpdateService_StateChanged;
        var clipboard = new WpfClipboardService();
        _clipboardService = clipboard;
        var router = new WebMessageRouter(
            new ShellExternalLauncher(),
            clipboard,
            RefreshAsync,
            OpenChangesAsync,
            DismissNoticeAsync,
            ReadyAsync,
            () => _appUpdateService.ApplyAndRestart(),
            () => ShellFolderLauncher.Open(paths.LogDirectory),
            _logger,
            MarkHistorySeenAsync,
            new WebPreferenceHandlers(
                SetGroupsAsync,
                SetThemeAsync,
                SetPublicHistoryAsync,
                HideGroupSuggestionAsync,
                SaveProfileAsync,
                SkipProfileAsync),
            CompareWithAsync,
            CopyBatchAsync,
            CopyNoticeAsync,
            CopyComparisonAsync);
        await _window.InitializeBrowserAsync(router, ReportCommandFailureAsync, cancellationToken);
        _logger.Log(AppLogLevel.Info, "bootstrap", $"Приложение {ProductInfo.Version} готово к работе.");
    }

    public void Dispose()
    {
        if (_appUpdateService is not null)
        {
            _appUpdateService.StateChanged -= AppUpdateService_StateChanged;
            _appUpdateService.Dispose();
        }
        _updateService?.Dispose();
        _store?.Dispose();
        _httpClient.Dispose();
    }

    private async Task ReadyAsync(CancellationToken cancellationToken)
    {
        _status = new AppStatusViewModel("checking", "Проверяем обновления…");
        await SendStateAsync().ConfigureAwait(false);
        _ = RefreshAsync(cancellationToken);
        if (_appUpdateService is not null)
        {
            _ = _appUpdateService.CheckAndDownloadAsync(cancellationToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_updateService is null || _store is null) return;
        _status = new AppStatusViewModel("checking", "Проверяем обновления…");
        await SendStateAsync().ConfigureAwait(false);
        await SyncPublicHistoryAsync(cancellationToken).ConfigureAwait(false);
        var previousRetrievedAt = _snapshot?.RetrievedAt;
        var result = await _updateService.CheckAsync(cancellationToken).ConfigureAwait(false);
        _snapshot = result.Snapshot ?? _snapshot;
        LogGroupMapConflicts();
        _comparison = null;
        var storedHistory = await _store.LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
        _history = ChangeHistoryMerger.Merge(storedHistory, _publicHistory);
        await _store.SaveHistoryAsync(_history, cancellationToken).ConfigureAwait(false);
        _archives = await BuildArchiveListAsync(cancellationToken).ConfigureAwait(false);
        _notice = null;
        _noticeRelatedBatchIds = [];
        _toast = null;
        await ApplyGroupRenamesAsync(cancellationToken).ConfigureAwait(false);
        if (result.Status == CalendarUpdateStatus.Updated && result.Batch is not null && _updatePresentationPolicy is not null)
        {
            var presentation = _updatePresentationPolicy.Evaluate(result.Batch, _state);
            _notice = presentation.Notice;
            _toast = presentation.Toast ?? _toast;
            _noticeRelatedBatchIds = previousRetrievedAt is null
                ? [result.Batch.Id]
                : _history.Batches
                    .Where(batch => batch.CheckedAt > previousRetrievedAt.Value)
                    .Select(batch => batch.Id)
                    .DefaultIfEmpty(result.Batch.Id)
                    .ToArray();
            if (presentation.MarkSeen && _stateStore is not null)
            {
                _state = _state.WithSeen([result.Batch.Id]);
                await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
            }
        }
        _status = result.Status switch
        {
            CalendarUpdateStatus.NoChanges => new AppStatusViewModel("ready", "Данные актуальны"),
            CalendarUpdateStatus.Updated => new AppStatusViewModel("updated", "Календарь обновлён"),
            CalendarUpdateStatus.Rejected => _fallbackStatus ?? new AppStatusViewModel("error", "Обновление отклонено"),
            CalendarUpdateStatus.Failed => _fallbackStatus ?? new AppStatusViewModel("error", "Не удалось обновить"),
            _ => new AppStatusViewModel("ready", result.UserMessage)
        };
        if (result.Status is CalendarUpdateStatus.NoChanges or CalendarUpdateStatus.Updated)
        {
            _fallbackStatus = null;
        }
        await SendStateAsync().ConfigureAwait(false);
        _toast = null;
    }

    private async Task OpenChangesAsync(string batchId)
    {
        await DismissNoticeAsync(batchId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DismissNoticeAsync(string batchId, CancellationToken cancellationToken)
    {
        if (_stateStore is null || string.IsNullOrWhiteSpace(batchId)) return;
        _state = _state.WithSeen([batchId]);
        _notice = null;
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
    }

    private Task SendStateAsync()
    {
        if (_snapshot is null || _viewModelFactory is null) return Task.CompletedTask;
        var model = _viewModelFactory.Create(
            _snapshot,
            _history,
            _status,
            _notice,
            _state,
            _appUpdateService?.State,
            _toast,
            _archives,
            _comparison,
            _noticeRelatedBatchIds,
            _groupMap);
        return _window.Dispatcher.InvokeAsync(() => _window.PostStateAsync(model)).Task.Unwrap();
    }

    private async Task ReportCommandFailureAsync(string message)
    {
        _toast = new ToastViewModel("error", message);
        await SendStateAsync().ConfigureAwait(false);
        _toast = null;
    }

    private async Task MarkHistorySeenAsync(CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _state.WithSeen(_history.Batches.Select(batch => batch.Id));
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendStateAsync().ConfigureAwait(false);
    }

    private async Task SetGroupsAsync(IReadOnlyList<string> groups, CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _groupMap is null
            ? _state.WithGroups(groups)
            : _state.WithGroupPreferences(
                groups,
                GroupSelectionCalculator.CaptureOverrides(_groupMap, _state.SelectedSectors, groups));
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveProfileAsync(WebProfileSelection profile, CancellationToken cancellationToken)
    {
        if (_stateStore is null || _groupMap is null) return;
        var knownSectors = _groupMap.Sectors.Select(sector => sector.Id).ToHashSet(StringComparer.Ordinal);
        var sectors = profile.Sectors.Where(knownSectors.Contains).ToArray();
        var manual = GroupSelectionCalculator.CaptureOverrides(_groupMap, sectors, profile.Groups);
        var selected = GroupSelectionCalculator.Calculate(_groupMap, sectors, manual);
        _state = _state.WithProfile(profile.Roles, sectors, manual, selected);
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendStateAsync().ConfigureAwait(false);
    }

    private async Task SkipProfileAsync(CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _state.CompleteOnboarding();
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendStateAsync().ConfigureAwait(false);
    }

    private async Task SetThemeAsync(string theme, CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _state.WithTheme(theme);
        _window.ApplyTitleBarTheme(theme);
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
    }

    private async Task SetPublicHistoryAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _state.WithPublicHistory(enabled);
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendStateAsync().ConfigureAwait(false);
    }

    private async Task HideGroupSuggestionAsync(string key, CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        _state = _state.WithHiddenGroupSuggestions((_state.HiddenGroupSuggestions ?? []).Append(key));
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        await SendStateAsync().ConfigureAwait(false);
    }

    private async Task ApplyGroupRenamesAsync(CancellationToken cancellationToken)
    {
        if (_stateStore is null) return;
        var update = GroupSubscriptionUpdater.Apply(_state, _history.Batches);
        if (update.AppliedRenames.Count == 0) return;
        _state = update.State.WithSeen(update.AppliedRenames.Select(item => item.BatchId));
        await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
        foreach (var applied in update.AppliedRenames)
        {
            var rename = applied.Rename;
            _logger.Log(AppLogLevel.Info, "groups", $"Подписка перенесена: «{rename.From}» → «{rename.To}».");
        }
        var latest = update.AppliedRenames[^1].Rename;
        _toast = new ToastViewModel("success", $"Группа «{latest.From}» переименована в «{latest.To}», подписка перенесена");
    }

    private async Task SyncPublicHistoryAsync(CancellationToken cancellationToken)
    {
        if (_publicHistoryClient is null
            || _store is null
            || _stateStore is null
            || _snapshot is null
            || !PublicHistorySyncPolicy.ShouldSync(_state, TimeProvider.System.GetUtcNow()))
        {
            return;
        }

        try
        {
            var result = await _publicHistoryClient.FetchAsync(cancellationToken).ConfigureAwait(false);
            if (_groupMapStore is not null)
            {
                await _groupMapStore.SaveAsync(result.Groups, cancellationToken).ConfigureAwait(false);
                _groupMap = result.Groups;
                LogGroupMapConflicts();
            }
            var local = await _store.LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
            _publicHistory = result.History;
            _history = ChangeHistoryMerger.Merge(local, _publicHistory);
            await _store.SaveHistoryAsync(_history, cancellationToken).ConfigureAwait(false);
            await ApplyGroupRenamesAsync(cancellationToken).ConfigureAwait(false);
            var syncedAt = TimeProvider.System.GetUtcNow();
            _state = PublicHistorySyncPolicy.Apply(_state, _publicHistory, _snapshot.RetrievedAt, syncedAt);
            await _stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
            await SendStateAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is PublicHistoryException or IOException or UnauthorizedAccessException)
        {
            _logger.Log(AppLogLevel.Warning, "public-history", "Не удалось обновить общую историю изменений.", error);
        }
    }

    private async Task<bool> CompareWithAsync(string id, CancellationToken cancellationToken)
    {
        if (_store is null || _snapshot is null || _summaryFactory is null || _diffEngine is null) return false;
        var baseline = id.Equals("bundled", StringComparison.Ordinal)
            ? _bundledSnapshot
            : await _store.LoadArchiveAsync(id, cancellationToken).ConfigureAwait(false);
        if (baseline is null) return false;
        var selectedGroups = new HashSet<string>(_state.SelectedGroups, StringComparer.OrdinalIgnoreCase);
        var changes = _diffEngine.Compare(baseline.Events, _snapshot.Events);
        var summary = _summaryFactory.Create(
            changes,
            int.MaxValue,
            DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            selectedGroups,
            UserRoleCategories.For(_state.Roles));
        _comparison = new SnapshotComparison(baseline.RetrievedAt, summary);
        await SendStateAsync().ConfigureAwait(false);
        return true;
    }

    private Task<bool> CopyBatchAsync(string batchId, CancellationToken cancellationToken) =>
        CopyStoredBatchAsync(batchId, cancellationToken);

    private Task<bool> CopyNoticeAsync(string batchId, CancellationToken cancellationToken) =>
        CopyStoredBatchAsync(batchId, cancellationToken);

    private async Task<bool> CopyStoredBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_summaryFactory is null || _clipboardService is null) return false;
        var batch = _history.Batches.FirstOrDefault(item => item.Id.Equals(batchId, StringComparison.Ordinal));
        if (batch is null) return false;
        var selectedGroups = new HashSet<string>(_state.SelectedGroups, StringComparer.OrdinalIgnoreCase);
        var summary = _summaryFactory.Create(
            batch.Changes,
            int.MaxValue,
            DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime),
            selectedGroups,
            UserRoleCategories.For(_state.Roles));
        _clipboardService.SetText(ChangeSummaryTextFormatter.Format(summary, batch.CheckedAt, selectedGroups));
        await ShowCopiedToastAsync().ConfigureAwait(false);
        return true;
    }

    private async Task<bool> CopyComparisonAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_comparison is null || _snapshot is null || _clipboardService is null) return false;
        var selectedGroups = new HashSet<string>(_state.SelectedGroups, StringComparer.OrdinalIgnoreCase);
        _clipboardService.SetText(ChangeSummaryTextFormatter.Format(_comparison.Summary, _snapshot.RetrievedAt, selectedGroups));
        await ShowCopiedToastAsync().ConfigureAwait(false);
        return true;
    }

    private async Task ShowCopiedToastAsync()
    {
        _toast = new ToastViewModel("success", "Сводка скопирована");
        await SendStateAsync().ConfigureAwait(false);
        _toast = null;
    }

    private async Task<IReadOnlyList<SnapshotArchiveInfo>> BuildArchiveListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SnapshotArchiveInfo> stored = _store is null
            ? []
            : await _store.ListArchivesAsync(cancellationToken).ConfigureAwait(false);
        return _bundledSnapshot is null
            ? stored
            : stored.Append(new SnapshotArchiveInfo("bundled", _bundledSnapshot.RetrievedAt)).ToArray();
    }

    private void AppUpdateService_StateChanged(object? sender, AppUpdateState state)
    {
        _ = SendStateAsync();
    }

    private static async Task<CalendarSnapshot> LoadBundledAsync(
        IEventNormalizer normalizer,
        CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarkingCalendar.Resources.bundled-source.json")
            ?? throw new FileNotFoundException("Встроенный снимок календаря не найден.");
        await using var metadata = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarkingCalendar.Resources.bundled-metadata.json")
            ?? throw new FileNotFoundException("Метаданные встроенного снимка календаря не найдены.");
        return await new BundledSnapshotLoader(normalizer).LoadAsync(
            stream,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ChangeHistory> LoadBundledHistoryAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarkingCalendar.Resources.bundled-history.json")
            ?? throw new FileNotFoundException("Встроенная история изменений календаря не найдена.");
        return await BundledSnapshotLoader.LoadHistoryAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<GroupMap> LoadBundledGroupsAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarkingCalendar.Resources.bundled-groups.json")
            ?? throw new FileNotFoundException("Встроенная карта товарных групп не найдена.");
        return await BundledSnapshotLoader.LoadGroupsAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private void LogGroupMapConflicts()
    {
        if (_groupMap is null || _snapshot is null) return;
        foreach (var match in GroupMapMatcher.Match(_groupMap, _snapshot.Events).Matches.Where(item => item.NameConflict))
        {
            _logger.Log(
                AppLogLevel.Warning,
                "groups",
                $"Группа «{match.SnapshotGroup}» сопоставлена по ссылке с записью карты «{match.Entry.Name}».");
        }
    }
}
