using MarkingCalendar.Infrastructure.Diagnostics;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace MarkingCalendar.App.Updates;

public sealed class VelopackUpdateSource(
    IAppLogger? logger = null,
    UpdateManager? primary = null,
    UpdateManager? fallback = null) : IAppUpdateSource
{
    private readonly UpdateManager _primary = primary ?? new(new SimpleWebSource(ProductInfo.UpdateFeedUrl));
    private readonly UpdateManager _fallback = fallback ?? new(
        new GithubSource(ProductInfo.RepositoryUrl, null, false));

    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_primary.IsInstalled)
        {
            throw new AppUpdateUnavailableException("Приложение запущено не из установки Velopack.");
        }

        try
        {
            try
            {
                return await CheckSourceAsync(_primary, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not NotInstalledException && !cancellationToken.IsCancellationRequested)
            {
                logger?.Log(AppLogLevel.Warning, "app-update",
                    "Канал raw.githubusercontent.com недоступен. Проверяем GitHub Releases.", error);
                return await CheckSourceAsync(_fallback, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (NotInstalledException error)
        {
            throw new AppUpdateUnavailableException("Приложение запущено не из установки Velopack.", error);
        }
    }

    private static async Task<AppUpdateRelease?> CheckSourceAsync(UpdateManager manager, CancellationToken cancellationToken)
    {
        var update = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return update is null
            ? null
            : new AppUpdateRelease(update.TargetFullRelease.Version.ToString(), new DownloadSource(manager, update));
    }

    public Task DownloadAsync(
        AppUpdateRelease release,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        if (release.Token is not DownloadSource source)
        {
            throw new ArgumentException("Неизвестный формат обновления.", nameof(release));
        }

        return source.Manager.DownloadUpdatesAsync(source.Update, progress.Report, cancellationToken);
    }

    public void ApplyAndRestart(AppUpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Token is not DownloadSource source)
        {
            throw new ArgumentException("Неизвестный формат обновления.", nameof(release));
        }

        source.Manager.ApplyUpdatesAndRestart(source.Update.TargetFullRelease);
    }

    private sealed record DownloadSource(UpdateManager Manager, UpdateInfo Update);
}
