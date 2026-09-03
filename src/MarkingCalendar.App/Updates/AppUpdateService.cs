using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.App.Updates;

public enum AppUpdateStage
{
    Idle,
    Checking,
    NoUpdate,
    Downloading,
    ReadyToRestart,
    Failed,
    Unavailable
}

public sealed record AppUpdateState(
    AppUpdateStage Stage,
    string Message,
    int? Progress = null,
    string? Version = null)
{
    public static AppUpdateState Initial { get; } = new(AppUpdateStage.Idle, "Обновление ещё не проверялось");
}

public delegate void AppUpdateStateChangedHandler(object? sender, AppUpdateState state);

public sealed class AppUpdateService(
    IAppUpdateSource source,
    IAppLogger? logger = null,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly IAppUpdateSource _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IAppLogger? _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppUpdateRelease? _downloadedRelease;

    public AppUpdateState State { get; private set; } = AppUpdateState.Initial;
    public event AppUpdateStateChangedHandler? StateChanged;

    public async Task CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(new AppUpdateState(AppUpdateStage.Checking, "Проверка обновления приложения…"));
            var release = await _source.CheckAsync(cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                Publish(new AppUpdateState(AppUpdateStage.NoUpdate, "Установлена последняя версия"));
                return;
            }

            Publish(new AppUpdateState(AppUpdateStage.Downloading, "Загрузка обновления…", 0, release.Version));
            var progress = new CoalescingProgress(
                _timeProvider,
                value => Publish(new AppUpdateState(
                    AppUpdateStage.Downloading,
                    "Загрузка обновления…",
                    value,
                    release.Version)));
            await _source.DownloadAsync(release, progress, cancellationToken).ConfigureAwait(false);
            _downloadedRelease = release;
            Publish(new AppUpdateState(
                AppUpdateStage.ReadyToRestart,
                "Обновление готово к установке",
                100,
                release.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppUpdateUnavailableException)
        {
            Publish(new AppUpdateState(
                AppUpdateStage.Unavailable,
                "Автообновление доступно в установленной версии"));
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "app-update", "Не удалось проверить обновление приложения.", error);
            Publish(new AppUpdateState(
                AppUpdateStage.Failed,
                "Не удалось подключиться к GitHub. Проверим снова при следующем запуске"));
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool ApplyAndRestart()
    {
        if (_downloadedRelease is null || State.Stage != AppUpdateStage.ReadyToRestart)
        {
            return false;
        }

        try
        {
            _source.ApplyAndRestart(_downloadedRelease);
            return true;
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "app-update", "Не удалось установить обновление приложения.", error);
            Publish(new AppUpdateState(AppUpdateStage.Failed, "Не удалось установить обновление приложения"));
            return false;
        }
    }

    private void Publish(AppUpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class CoalescingProgress(TimeProvider timeProvider, Action<int> callback) : IProgress<int>
    {
        private readonly TimeProvider _timeProvider = timeProvider;
        private readonly Action<int> _callback = callback;
        private readonly object _gate = new();
        private DateTimeOffset _lastPublishedAt = timeProvider.GetUtcNow();
        private int _lastProgress;

        public void Report(int value)
        {
            int? publishedProgress = null;
            lock (_gate)
            {
                var progress = Math.Clamp(value, 0, 100);
                if (progress <= _lastProgress) return;
                var now = _timeProvider.GetUtcNow();
                if (progress < 100
                    && progress - _lastProgress < 5
                    && now - _lastPublishedAt < TimeSpan.FromSeconds(1))
                {
                    return;
                }

                _lastProgress = progress;
                _lastPublishedAt = now;
                publishedProgress = progress;
            }

            if (publishedProgress is { } progressToPublish) _callback(progressToPublish);
        }
    }
}
