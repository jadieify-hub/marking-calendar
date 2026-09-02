namespace MarkingCalendar.Infrastructure.Storage;

public sealed class RetentionPolicy(int maxArchives = 20, int maxLogs = 30)
{
    private readonly int _maxArchives = Positive(maxArchives, nameof(maxArchives));
    private readonly int _maxLogs = Positive(maxLogs, nameof(maxLogs));

    public void Enforce(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Trim(paths.ArchiveDirectory, _maxArchives);
        Trim(paths.LogDirectory, _maxLogs);
    }

    private static void Trim(string directory, int keep)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var staleFiles = new DirectoryInfo(directory)
            .EnumerateFiles()
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();
        foreach (var file in staleFiles)
        {
            file.Delete();
        }
    }

    private static int Positive(int value, string parameterName) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(parameterName, "Лимит хранения должен быть больше нуля.");
}

