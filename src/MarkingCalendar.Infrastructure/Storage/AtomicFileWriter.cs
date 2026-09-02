using System.Text.Json;

namespace MarkingCalendar.Infrastructure.Storage;

public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public async Task WriteJsonAsync<T>(string destination, T value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Не удалось определить каталог назначения.", nameof(destination));
        Directory.CreateDirectory(directory);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var validationStream = File.OpenRead(temporary))
            {
                var roundTrip = await JsonSerializer.DeserializeAsync<T>(validationStream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                if (roundTrip is null)
                {
                    throw new JsonException("Проверка временного файла после записи не пройдена.");
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task WriteTextAsync(string destination, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Не удалось определить каталог назначения.", nameof(destination));
        Directory.CreateDirectory(directory);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, value, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
