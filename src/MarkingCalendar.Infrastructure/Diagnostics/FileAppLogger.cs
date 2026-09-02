using System.Globalization;
using System.Text;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Diagnostics;

public sealed class FileAppLogger(AppPaths paths, TimeProvider timeProvider) : IAppLogger
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly object _gate = new();

    public void Log(AppLogLevel level, string source, string message, Exception? exception = null)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.LogDirectory);
                var timestamp = _timeProvider.GetUtcNow();
                var path = Path.Combine(_paths.LogDirectory, $"app-{timestamp:yyyy-MM-dd}.log");
                var line = FormatLine(timestamp, level, source, message, exception);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Utf8WithoutBom);
                writer.WriteLine(line);
            }
        }
        catch (Exception)
        {
            // Diagnostics must never make the application unusable.
        }
    }

    public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.LogDirectory);
                var timestamp = _timeProvider.GetUtcNow();
                var stem = $"rejected-{SafeFilePart(source)}-{timestamp:yyyyMMdd-HHmmssfff}";
                var path = AvailablePath(stem);
                File.WriteAllText(path, json ?? string.Empty, Utf8WithoutBom);
            }
        }
        catch (Exception)
        {
            // Rejected payload capture is best-effort diagnostics.
        }

        return Task.CompletedTask;
    }

    private string AvailablePath(string stem)
    {
        var path = Path.Combine(_paths.LogDirectory, $"{stem}.json");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(_paths.LogDirectory, $"{stem}-{suffix.ToString(CultureInfo.InvariantCulture)}.json");
        }

        return path;
    }

    private static string FormatLine(
        DateTimeOffset timestamp,
        AppLogLevel level,
        string source,
        string message,
        Exception? exception)
    {
        var line = $"{timestamp:O} [{level.ToString().ToUpperInvariant()}] {SingleLine(source)}: {SingleLine(message)}";
        return exception is null
            ? line
            : $"{line} | {exception.GetType().Name}: {SingleLine(exception.Message)}";
    }

    private static string SafeFilePart(string value)
    {
        var normalized = SingleLine(value).ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                result.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                result.Append('-');
                previousWasSeparator = true;
            }
        }

        return result.ToString().Trim('-') is { Length: > 0 } safe ? safe : "payload";
    }

    private static string SingleLine(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
