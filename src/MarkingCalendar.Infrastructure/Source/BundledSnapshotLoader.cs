using System.Text.Json;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Core.Snapshots;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Source;

public sealed class BundledSnapshotLoader(IEventNormalizer normalizer)
{
    private readonly CalendarPayloadParser _parser = new(normalizer ?? throw new ArgumentNullException(nameof(normalizer)));

    public async Task<CalendarSnapshot> LoadAsync(
        Stream source,
        Stream metadataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadataSource);
        BundledSnapshotMetadata? metadata;
        try
        {
            metadata = await JsonSerializer.DeserializeAsync<BundledSnapshotMetadata>(
                metadataSource,
                JsonDefaults.Options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Метаданные встроенного снимка повреждены.", error);
        }

        if (metadata is null
            || metadata.RetrievedAt == default
            || metadata.ItemCount < 1
            || !Uri.TryCreate(metadata.SourceUrl, UriKind.Absolute, out var sourceUrl)
            || (sourceUrl.Scheme != Uri.UriSchemeHttps && sourceUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidDataException("Метаданные встроенного снимка некорректны.");
        }

        var events = await _parser.ParseAsync(source, cancellationToken).ConfigureAwait(false);
        if (events.Count != metadata.ItemCount)
        {
            throw new InvalidDataException(
                $"Встроенный снимок содержит {events.Count} событий, но в метаданных указано число событий {metadata.ItemCount}.");
        }

        return CalendarSnapshot.Create(metadata.RetrievedAt, sourceUrl, events);
    }

    public static async Task<ChangeHistory> LoadHistoryAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ChangeHistory? history;
        try
        {
            history = await JsonSerializer.DeserializeAsync<ChangeHistory>(
                source,
                JsonDefaults.Options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Встроенная история изменений повреждена.", error);
        }

        if (history?.Batches is null
            || history.Batches.Count > 500
            || history.Batches.Any(batch => string.IsNullOrWhiteSpace(batch.Id)
                || batch.CheckedAt == default
                || batch.Changes is null))
        {
            throw new InvalidDataException("Встроенная история изменений некорректна.");
        }

        return new ChangeHistory(history.Batches
            .Select(batch => batch with { Source = ChangeBatchSources.Public })
            .ToArray());
    }

    public static async Task<GroupMap> LoadGroupsAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var map = await JsonSerializer.DeserializeAsync<GroupMap>(source, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            GroupMapValidator.EnsureValid(map);
            return map!;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or GroupMapValidationException)
        {
            throw new InvalidDataException("Встроенная карта товарных групп повреждена.", error);
        }
    }

    private sealed record BundledSnapshotMetadata(DateTimeOffset RetrievedAt, string SourceUrl, int ItemCount);
}
