namespace MarkingCalendar.Infrastructure.Diagnostics;

public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public interface IAppLogger
{
    void Log(AppLogLevel level, string source, string message, Exception? exception = null);

    Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken);
}
