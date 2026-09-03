namespace MarkingCalendar.Infrastructure.Storage;

public enum AppStorageLayout
{
    Application,
    Flat
}

public sealed class AppPaths
{
    private readonly AppStorageLayout _layout;

    public AppPaths(string rootDirectory, AppStorageLayout layout = AppStorageLayout.Application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        _layout = layout;
    }

    public string RootDirectory { get; }
    public string DataDirectory => _layout == AppStorageLayout.Flat ? RootDirectory : Path.Combine(RootDirectory, "data");
    public string CurrentSnapshot => Path.Combine(DataDirectory, "current.json");
    public string BundledSnapshot => Path.Combine(DataDirectory, "bundled.json");
    public string GroupMapFile => Path.Combine(DataDirectory, "groups.json");
    public string HistoryDirectory => Path.Combine(RootDirectory, "history");
    public string ChangeHistoryFile => Path.Combine(HistoryDirectory, "changes.json");
    public string ArchiveDirectory => Path.Combine(RootDirectory, "archive");
    public string LogDirectory => Path.Combine(RootDirectory, "logs");
    public string StateFile => Path.Combine(RootDirectory, "state.json");
    public string WindowStateFile => Path.Combine(RootDirectory, "window-state.json");
    public string BrowserDataDirectory => Path.Combine(RootDirectory, "webview2");
    public string MigrationMarker => Path.Combine(RootDirectory, "migration-v1.json");

    public static AppPaths ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KRS",
        "MarkingCalendar"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(ArchiveDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
