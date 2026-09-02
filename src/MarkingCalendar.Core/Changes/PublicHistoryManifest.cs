namespace MarkingCalendar.Core.Changes;

public sealed record PublicHistoryFiles(
    string Current = "current.json",
    string Source = "source.json",
    string History = "history/changes.json",
    string Changelog = "CHANGELOG.md",
    string Feed = "feed.xml");

public sealed record PublicHistoryManifest(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    string SnapshotId,
    int EventCount,
    int BatchCount,
    PublicHistoryFiles Files,
    string GroupsUrl = "groups.json");
