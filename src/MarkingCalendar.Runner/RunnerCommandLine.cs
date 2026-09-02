using MarkingCalendar.Infrastructure.Source;

namespace MarkingCalendar.Runner;

public abstract record RunnerCommand;

public sealed record CheckCommand(
    string DataDirectory,
    Uri SourceUrl,
    bool DryRun,
    bool AcceptAnomaly) : RunnerCommand;

public sealed record RenderTelegramCommand(string DataDirectory, string BatchId) : RunnerCommand;
public sealed record ValidateGroupsCommand(string DataDirectory) : RunnerCommand;

public sealed class RunnerCommandLineException(string message) : Exception(message);

public static class RunnerCommandLine
{
    public static RunnerCommand Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            throw Usage();
        }

        if (args[0].Equals("render-telegram", StringComparison.OrdinalIgnoreCase))
        {
            return ParseTelegram(args);
        }
        if (args[0].Equals("validate-groups", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGroupValidation(args);
        }
        if (!args[0].Equals("check", StringComparison.OrdinalIgnoreCase)) throw Usage();

        string? dataDirectory = null;
        var sourceUrl = MarkingCalendarClient.Endpoint;
        var dryRun = false;
        var acceptAnomaly = false;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--data" when index + 1 < args.Count:
                    dataDirectory = args[++index];
                    break;
                case "--source" when index + 1 < args.Count:
                    if (!Uri.TryCreate(args[++index], UriKind.Absolute, out sourceUrl)
                        || (!sourceUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                            && !sourceUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new RunnerCommandLineException("Параметр --source должен содержать абсолютный HTTP(S)-адрес.");
                    }

                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--accept-anomaly":
                    acceptAnomaly = true;
                    break;
                default:
                    throw new RunnerCommandLineException($"Неизвестный или неполный параметр: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new RunnerCommandLineException("Не указан обязательный параметр --data <каталог>.");
        }

        return new CheckCommand(dataDirectory, sourceUrl, dryRun, acceptAnomaly);
    }

    private static RenderTelegramCommand ParseTelegram(IReadOnlyList<string> args)
    {
        string? dataDirectory = null;
        string? batchId = null;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--data" when index + 1 < args.Count:
                    dataDirectory = args[++index];
                    break;
                case "--batch" when index + 1 < args.Count:
                    batchId = args[++index];
                    break;
                default:
                    throw new RunnerCommandLineException($"Неизвестный или неполный параметр: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(dataDirectory) || string.IsNullOrWhiteSpace(batchId))
        {
            throw new RunnerCommandLineException("Использование: render-telegram --batch <id> --data <каталог>");
        }
        return new RenderTelegramCommand(dataDirectory, batchId);
    }

    private static ValidateGroupsCommand ParseGroupValidation(IReadOnlyList<string> args)
    {
        if (args.Count == 3
            && args[1].Equals("--data", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(args[2]))
        {
            return new ValidateGroupsCommand(args[2]);
        }
        throw new RunnerCommandLineException("Использование: validate-groups --data <каталог>");
    }

    private static RunnerCommandLineException Usage() => new(
        "Использование: check --data <каталог> [--source <url>] [--dry-run] [--accept-anomaly] | render-telegram --batch <id> --data <каталог> | validate-groups --data <каталог>");
}
