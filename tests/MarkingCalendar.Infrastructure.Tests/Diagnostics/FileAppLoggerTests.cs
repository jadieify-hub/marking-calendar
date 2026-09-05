using System.Text;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Tests.Diagnostics;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void Log_PreservesEntireInnerExceptionChainOnOneLine()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var logger = new FileAppLogger(paths, new FixedTimeProvider());
        var error = new HttpRequestException("TLS failed\r\nsee inner exception",
            new System.Security.Authentication.AuthenticationException("Authentication\tfailed",
                new IOException("unexpected\nEOF")));

        logger.Log(AppLogLevel.Error, "app-update", "Проверка не удалась", error);

        var logPath = Assert.Single(Directory.GetFiles(paths.LogDirectory, "app-*.log"));
        var line = Assert.Single(File.ReadAllLines(logPath));
        Assert.Contains("HttpRequestException: TLS failed see inner exception", line, StringComparison.Ordinal);
        Assert.Contains("AuthenticationException: Authentication failed", line, StringComparison.Ordinal);
        Assert.Contains("IOException: unexpected EOF", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_WritesOneSanitizedReadableLine()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var logger = new FileAppLogger(paths, new FixedTimeProvider());

        logger.Log(
            AppLogLevel.Error,
            "calendar-update",
            "Не удалось\r\nобновить",
            new InvalidOperationException("первая строка\nвторая строка"));

        var logPath = Assert.Single(Directory.GetFiles(paths.LogDirectory, "app-*.log"));
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        logger.Log(AppLogLevel.Info, "calendar-update", "Повторная запись при открытом файле");
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = reader.ReadToEnd();
        Assert.Contains("2026-09-02T07:05:06.0000000+00:00 [ERROR] calendar-update: Не удалось обновить", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: первая строка вторая строка", line, StringComparison.Ordinal);
        Assert.Equal(2, line.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task SaveRejectedJsonAsync_UsesSafeTimestampedNameAndPreservesPayload()
    {
        using var temp = new TemporaryDirectory();
        var paths = new AppPaths(temp.Path);
        var logger = new FileAppLogger(paths, new FixedTimeProvider());
        const string payload = "{\"data\":\"исходный ответ\"}";

        await logger.SaveRejectedJsonAsync("calendar/source", payload, CancellationToken.None);

        var rejected = Assert.Single(Directory.GetFiles(paths.LogDirectory, "rejected-*.json"));
        Assert.Equal("rejected-calendar-source-20260902-070506000.json", Path.GetFileName(rejected));
        Assert.Equal(payload, await File.ReadAllTextAsync(rejected, Encoding.UTF8));
    }

    [Fact]
    public void Log_DoesNotCrashApplicationWhenDirectoryCannotBeCreated()
    {
        using var temp = new TemporaryDirectory();
        var occupiedRoot = Path.Combine(temp.Path, "occupied");
        File.WriteAllText(occupiedRoot, "file");
        var logger = new FileAppLogger(new AppPaths(occupiedRoot), new FixedTimeProvider());

        var error = Record.Exception(() => logger.Log(AppLogLevel.Info, "startup", "Запуск"));

        Assert.Null(error);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 5, 6, TimeSpan.Zero);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MarkingCalendar.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
