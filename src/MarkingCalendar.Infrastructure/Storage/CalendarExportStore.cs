using System.Text.Json;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed class CalendarExportStore(AppPaths paths, IAtomicFileWriter writer)
{
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    // Resolve against the whole calendar before filtering the exported events.
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IReadOnlyList<CalendarEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        ExportIdentity[] previous = [];
        if (File.Exists(_paths.ExportIdentityFile))
        {
            await using var stream = File.OpenRead(_paths.ExportIdentityFile);
            previous = await JsonSerializer.DeserializeAsync<ExportIdentity[]>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Не удалось прочитать идентификаторы экспорта.");
            if (previous.Any(item => item?.Event is null || string.IsNullOrWhiteSpace(item.Uid))
                || previous.Select(item => item.Event.Id).Distinct(StringComparer.Ordinal).Count() != previous.Length
                || previous.Select(item => item.Uid).Distinct(StringComparer.Ordinal).Count() != previous.Length)
            {
                throw new InvalidDataException("Сохранённые идентификаторы экспорта повреждены.");
            }
        }

        var previousById = previous.ToDictionary(item => item.Event.Id, StringComparer.Ordinal);
        // ponytail: matching follows diff heuristics; prefer stable source IDs if the source provides them.
        var matches = EventDiffEngine.CompareWithIdentities(previous.Select(item => item.Event).ToArray(), events).PreviousIds;
        var identities = events.Select(item => new ExportIdentity(
            item,
            matches.TryGetValue(item.Id, out var previousId)
                ? previousById[previousId].Uid
                : previous.Length == 0 ? item.Id : Guid.NewGuid().ToString("N"))).ToArray();
        await _writer.WriteJsonAsync(_paths.ExportIdentityFile, identities, cancellationToken).ConfigureAwait(false);
        return identities.ToDictionary(item => item.Event.Id, item => item.Uid, StringComparer.Ordinal);
    }

    private sealed record ExportIdentity(CalendarEvent Event, string Uid);
}
