using System.Reflection;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Source;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Runner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        RunnerCommand command;
        try
        {
            command = RunnerCommandLine.Parse(args);
        }
        catch (RunnerCommandLineException error)
        {
            await Console.Error.WriteLineAsync(error.Message).ConfigureAwait(false);
            return 1;
        }

        if (command is RenderTelegramCommand telegram)
        {
            try
            {
                var text = await TelegramAnnouncementRenderer.LoadAndRenderAsync(
                    telegram.DataDirectory,
                    telegram.BatchId,
                    CancellationToken.None).ConfigureAwait(false);
                await Console.Out.WriteAsync(text).ConfigureAwait(false);
                return 0;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"Не удалось сформировать сообщение Telegram: {error.Message}").ConfigureAwait(false);
                return 1;
            }
        }

        if (command is ValidateGroupsCommand validateGroups)
        {
            try
            {
                var result = await GroupMapCommandHandler.ExecuteAsync(validateGroups.DataDirectory, CancellationToken.None).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(result.Output).ConfigureAwait(false);
                return (int)result.ExitCode;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"Не удалось проверить карту товарных групп: {error.Message}").ConfigureAwait(false);
                return (int)HistoryRunnerExitCode.WriteError;
            }
        }

        var check = (CheckCommand)command;

        try
        {
            var assembly = typeof(Program).Assembly;
            await using var source = OpenResource(assembly, "MarkingCalendar.Resources.bundled-source.json");
            await using var metadata = OpenResource(assembly, "MarkingCalendar.Resources.bundled-metadata.json");
            var bundled = await new BundledSnapshotLoader(new EventNormalizer())
                .LoadAsync(source, metadata, CancellationToken.None)
                .ConfigureAwait(false);
            using var httpClient = new HttpClient();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.2";
            var runner = new HistoryRunner(
                new MarkingCalendarClient(httpClient, new EventNormalizer(), TimeProvider.System, version, check.SourceUrl),
                bundled,
                new SnapshotValidator(),
                new EventDiffEngine(),
                new AtomicFileWriter(),
                TimeProvider.System);
            var result = await runner.CheckAsync(
                new HistoryCheckOptions(check.DataDirectory, check.DryRun, check.AcceptAnomaly),
                CancellationToken.None).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(result.Output).ConfigureAwait(false);
            return (int)result.ExitCode;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            await Console.Error.WriteLineAsync($"Ошибка встроенного снимка: {error.Message}").ConfigureAwait(false);
            return (int)HistoryRunnerExitCode.WriteError;
        }
    }

    private static Stream OpenResource(Assembly assembly, string name) =>
        assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Ресурс {name} не найден.");
}
