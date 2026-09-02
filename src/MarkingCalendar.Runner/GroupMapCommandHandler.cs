using System.Text.Json;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Runner;

public static class GroupMapCommandHandler
{
    public static async Task<HistoryRunResult> ExecuteAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        try
        {
            var map = await ReadAsync<GroupMap>(Path.Combine(dataDirectory, "groups.json"), cancellationToken).ConfigureAwait(false);
            var errors = GroupMapValidator.Validate(map);
            if (errors.Count > 0)
            {
                return new HistoryRunResult(HistoryRunnerExitCode.Rejected, "REJECTED: " + string.Join(Environment.NewLine, errors));
            }

            var snapshot = await ReadAsync<CalendarSnapshot>(Path.Combine(dataDirectory, "current.json"), cancellationToken).ConfigureAwait(false);
            if (snapshot.Events is null)
            {
                return new HistoryRunResult(HistoryRunnerExitCode.Rejected, "REJECTED: Текущий снимок не содержит событий.");
            }

            var report = GroupMapMatcher.Match(map, snapshot.Events);
            var lines = new List<string> { "Карта товарных групп корректна." };
            lines.Add("Не размечены в карте: " + List(report.SnapshotOnly));
            lines.Add("Отсутствуют в снимке: " + List(report.MapOnly));
            lines.AddRange(report.Matches
                .Where(match => match.NameConflict)
                .Select(match => $"Предупреждение: ссылка {match.Link} сопоставила «{match.SnapshotGroup}» с записью карты «{match.Entry.Name}»."));
            return new HistoryRunResult(HistoryRunnerExitCode.Success, string.Join(Environment.NewLine, lines));
        }
        catch (JsonException error)
        {
            return new HistoryRunResult(HistoryRunnerExitCode.Rejected, $"REJECTED: JSON карты или снимка повреждён: {error.Message}");
        }
        catch (GroupMapValidationException error)
        {
            return new HistoryRunResult(HistoryRunnerExitCode.Rejected, "REJECTED: " + string.Join(Environment.NewLine, error.Errors));
        }
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException($"Файл {Path.GetFileName(path)} содержит пустой JSON.");
    }

    private static string List(IReadOnlyList<string> values) => values.Count == 0 ? "нет" : string.Join(", ", values);
}
