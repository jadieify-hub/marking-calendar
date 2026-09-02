using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace MarkingCalendar.App.Updates;

public sealed class VelopackUpdateSource : IAppUpdateSource
{
    private readonly UpdateManager _manager = new(
        new GithubSource(ProductInfo.RepositoryUrl, null, false));

    public async Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!_manager.IsInstalled)
        {
            throw new AppUpdateUnavailableException("Приложение запущено не из установки Velopack.");
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return update is null
                ? null
                : new AppUpdateRelease(update.TargetFullRelease.Version.ToString(), update);
        }
        catch (NotInstalledException error)
        {
            throw new AppUpdateUnavailableException("Приложение запущено не из установки Velopack.", error);
        }
    }

    public Task DownloadAsync(
        AppUpdateRelease release,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        if (release.Token is not UpdateInfo update)
        {
            throw new ArgumentException("Неизвестный формат обновления.", nameof(release));
        }

        return _manager.DownloadUpdatesAsync(update, progress.Report, cancellationToken);
    }

    public void ApplyAndRestart(AppUpdateRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Token is not UpdateInfo update)
        {
            throw new ArgumentException("Неизвестный формат обновления.", nameof(release));
        }

        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
