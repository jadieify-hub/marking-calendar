using System.Text.Json;
using MarkingCalendar.Core.Groups;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed class GroupMapStore(AppPaths paths, IAtomicFileWriter writer)
{
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<GroupMap?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.GroupMapFile)) return null;
        try
        {
            await using var stream = File.OpenRead(_paths.GroupMapFile);
            var map = await JsonSerializer.DeserializeAsync<GroupMap>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            GroupMapValidator.EnsureValid(map);
            return map;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or GroupMapValidationException)
        {
            throw new InvalidDataException("Сохранённая карта товарных групп повреждена.", error);
        }
    }

    public Task SaveAsync(GroupMap map, CancellationToken cancellationToken)
    {
        GroupMapValidator.EnsureValid(map);
        return _writer.WriteJsonAsync(_paths.GroupMapFile, map, cancellationToken);
    }
}
