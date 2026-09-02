namespace MarkingCalendar.App.Updates;

public sealed record AppUpdateRelease(string Version, object Token);

public interface IAppUpdateSource
{
    Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken);
    Task DownloadAsync(AppUpdateRelease release, IProgress<int> progress, CancellationToken cancellationToken);
    void ApplyAndRestart(AppUpdateRelease release);
}

public sealed class AppUpdateUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
