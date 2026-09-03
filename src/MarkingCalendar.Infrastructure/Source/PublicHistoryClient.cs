using System.Text.Json;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Groups;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.Infrastructure.Source;

public sealed class PublicHistoryException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed record PublicHistoryResult(
    ChangeHistory History,
    DateTimeOffset GeneratedAt,
    string SnapshotId,
    GroupMap Groups);

public sealed class PublicHistoryClient(
    HttpClient httpClient,
    Uri manifestUrl,
    string version = "0.1.5")
{
    private const int ManifestLimit = 1024 * 1024;
    private const int HistoryLimit = 10 * 1024 * 1024;
    private const int GroupsLimit = 1024 * 1024;
    private const string AllowedHost = "raw.githubusercontent.com";
    private const string AllowedPathPrefix = "/jadieify-hub/marking-calendar/data/";
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly Uri _manifestUrl = ValidateUrl(manifestUrl ?? throw new ArgumentNullException(nameof(manifestUrl)));
    private readonly string _version = string.IsNullOrWhiteSpace(version) ? "0.1.5" : version;

    public async Task<PublicHistoryResult> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var manifest = await ReadAsync<PublicHistoryManifest>(_manifestUrl, ManifestLimit, timeout.Token).ConfigureAwait(false);
            if (manifest.SchemaVersion != 1)
            {
                throw new PublicHistoryException($"Версия схемы общей истории не поддерживается: {manifest.SchemaVersion}.");
            }

            if (manifest.GeneratedAt == default
                || string.IsNullOrWhiteSpace(manifest.SnapshotId)
                || manifest.BatchCount < 0
                || manifest.BatchCount > 500
                || manifest.Files is null
                || string.IsNullOrWhiteSpace(manifest.Files.History)
                || string.IsNullOrWhiteSpace(manifest.GroupsUrl))
            {
                throw new PublicHistoryException("Манифест общей истории содержит некорректные данные.");
            }

            var historyUrl = ValidateUrl(new Uri(_manifestUrl, manifest.Files.History));
            var groupsUrl = ValidateUrl(new Uri(_manifestUrl, manifest.GroupsUrl));
            var history = await ReadAsync<ChangeHistory>(historyUrl, HistoryLimit, timeout.Token).ConfigureAwait(false);
            if (history.Batches is null || history.Batches.Count != manifest.BatchCount || history.Batches.Count > 500)
            {
                throw new PublicHistoryException("Общая история не соответствует манифесту.");
            }

            var groups = await ReadAsync<GroupMap>(groupsUrl, GroupsLimit, timeout.Token).ConfigureAwait(false);
            var groupErrors = GroupMapValidator.Validate(groups);
            if (groupErrors.Count > 0)
            {
                throw new PublicHistoryException("Карта товарных групп некорректна: " + string.Join(' ', groupErrors));
            }

            var publicHistory = new ChangeHistory(history.Batches
                .Select(batch => batch with { Source = ChangeBatchSources.Public })
                .ToArray());
            return new PublicHistoryResult(publicHistory, manifest.GeneratedAt, manifest.SnapshotId, groups);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PublicHistoryException)
        {
            throw;
        }
        catch (ArgumentException error)
        {
            throw new PublicHistoryException(error.Message, error);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            throw new PublicHistoryException("Не удалось загрузить общую историю изменений.", error);
        }
    }

    private async Task<T> ReadAsync<T>(Uri url, int byteLimit, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"MarkingCalendar/{_version}");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PublicHistoryException($"GitHub вернул HTTP {(int)response.StatusCode} при загрузке общей истории.");
        }

        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > byteLimit)
        {
            throw new PublicHistoryException("Превышен допустимый размер общей истории.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > byteLimit)
            {
                throw new PublicHistoryException("Превышен допустимый размер общей истории.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, JsonDefaults.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new PublicHistoryException("Общая история содержит пустой JSON.");
    }

    private static Uri ValidateUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || !url.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !url.IdnHost.Equals(AllowedHost, StringComparison.OrdinalIgnoreCase)
            || !url.AbsolutePath.StartsWith(AllowedPathPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Разрешена только официальная общая история проекта на GitHub.", nameof(url));
        }

        return url;
    }
}
