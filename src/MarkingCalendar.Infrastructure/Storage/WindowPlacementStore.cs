using System.Text.Json;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed record WindowPlacementState(
    double Left,
    double Top,
    double Width,
    double Height,
    bool Maximized);

public sealed class WindowPlacementStore(AppPaths paths, IAtomicFileWriter writer)
{
    private readonly AppPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IAtomicFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<WindowPlacementState?> LoadAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.WindowStateFile)) return null;
        await using var stream = File.OpenRead(_paths.WindowStateFile);
        return await JsonSerializer.DeserializeAsync<WindowPlacementState>(
            stream,
            JsonDefaults.Options,
            cancellationToken).ConfigureAwait(false);
    }

    public Task SaveAsync(WindowPlacementState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _writer.WriteJsonAsync(_paths.WindowStateFile, state, cancellationToken);
    }
}
