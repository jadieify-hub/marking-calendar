using System.Text.Json;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.App.Web;

public interface IExternalLauncher
{
    void Open(Uri uri);
}

public interface IClipboardService
{
    void SetText(string value);
}

public enum WebCommandKind
{
    Handled,
    Rejected,
    Failed
}

public sealed record WebCommandResult(WebCommandKind Kind, string? Message = null)
{
    public static WebCommandResult Handled { get; } = new(WebCommandKind.Handled);
    public static WebCommandResult Rejected { get; } = new(WebCommandKind.Rejected);
}

public sealed record WebProfileSelection(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Sectors,
    IReadOnlyList<string> Groups);

public sealed record WebPreferenceHandlers(
    Func<IReadOnlyList<string>, CancellationToken, Task> SetGroups,
    Func<string, CancellationToken, Task> SetTheme,
    Func<bool, CancellationToken, Task>? SetPublicHistory = null,
    Func<string, CancellationToken, Task>? HideGroupSuggestion = null,
    Func<WebProfileSelection, CancellationToken, Task>? SaveProfile = null,
    Func<CancellationToken, Task>? SkipProfile = null);

public sealed class WebMessageRouter(
    IExternalLauncher launcher,
    IClipboardService clipboard,
    Func<CancellationToken, Task> refresh,
    Func<string, Task> openChanges,
    Func<string, CancellationToken, Task> dismissNotice,
    Func<CancellationToken, Task>? ready = null,
    Func<bool>? restartForUpdate = null,
    Action? openLogs = null,
    IAppLogger? logger = null,
    Func<CancellationToken, Task>? markHistorySeen = null,
    WebPreferenceHandlers? preferences = null,
    Func<string, CancellationToken, Task<bool>>? compareWith = null,
    Func<string, CancellationToken, Task<bool>>? copyBatch = null,
    Func<string, CancellationToken, Task<bool>>? copyNotice = null,
    Func<CancellationToken, Task<bool>>? copyComparison = null,
    Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? exportCalendar = null)
{
    private readonly IExternalLauncher _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    private readonly IClipboardService _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    private readonly Func<CancellationToken, Task> _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    private readonly Func<string, Task> _openChanges = openChanges ?? throw new ArgumentNullException(nameof(openChanges));
    private readonly Func<string, CancellationToken, Task> _dismissNotice = dismissNotice ?? throw new ArgumentNullException(nameof(dismissNotice));
    private readonly Func<CancellationToken, Task> _ready = ready ?? (_ => Task.CompletedTask);
    private readonly Func<bool> _restartForUpdate = restartForUpdate ?? (() => false);
    private readonly Action? _openLogs = openLogs;
    private readonly IAppLogger? _logger = logger;
    private readonly Func<CancellationToken, Task> _markHistorySeen = markHistorySeen ?? (_ => Task.CompletedTask);
    private readonly WebPreferenceHandlers? _preferences = preferences;
    private readonly Func<string, CancellationToken, Task<bool>>? _compareWith = compareWith;
    private readonly Func<string, CancellationToken, Task<bool>>? _copyBatch = copyBatch;
    private readonly Func<string, CancellationToken, Task<bool>>? _copyNotice = copyNotice;
    private readonly Func<CancellationToken, Task<bool>>? _copyComparison = copyComparison;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? _exportCalendar = exportCalendar;

    public async Task<WebCommandResult> HandleAsync(string json, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(json)) return WebCommandResult.Rejected;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return WebCommandResult.Rejected;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeNode))
            {
                return WebCommandResult.Rejected;
            }

            var type = typeNode.GetString();
            try
            {
                switch (type)
                {
                    case "ready":
                        await _ready(cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "refresh":
                        await _refresh(cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "openChanges" when Text(root, "batchId") is { } batchId:
                        await _openChanges(batchId).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "dismissNotice" when Text(root, "batchId") is { } dismissedBatchId:
                        await _dismissNotice(dismissedBatchId, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "markHistorySeen":
                        await _markHistorySeen(cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "setGroups" when Groups(root) is { } groups && _preferences is not null:
                        await _preferences.SetGroups(groups, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "setTheme" when Theme(root) is { } theme && _preferences is not null:
                        await _preferences.SetTheme(theme, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "setPublicHistory" when Boolean(root, "enabled") is { } enabled && _preferences?.SetPublicHistory is not null:
                        await _preferences.SetPublicHistory(enabled, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "hideGroupSuggestion" when Text(root, "key") is { } groupKey && _preferences?.HideGroupSuggestion is not null:
                        var normalizedGroupKey = GroupKey.Normalize(groupKey);
                        if (normalizedGroupKey.Length == 0) return WebCommandResult.Rejected;
                        await _preferences.HideGroupSuggestion(normalizedGroupKey, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "saveProfile" when Profile(root) is { } profile && _preferences?.SaveProfile is not null:
                        await _preferences.SaveProfile(profile, cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "skipProfile" when _preferences?.SkipProfile is not null:
                        await _preferences.SkipProfile(cancellationToken).ConfigureAwait(false);
                        return WebCommandResult.Handled;
                    case "compareWith" when Text(root, "id") is { } archiveId && _compareWith is not null:
                        return await _compareWith(archiveId, cancellationToken).ConfigureAwait(false)
                            ? WebCommandResult.Handled
                            : WebCommandResult.Rejected;
                    case "copyBatch" when Text(root, "batchId") is { } copyBatchId && _copyBatch is not null:
                        return await _copyBatch(copyBatchId, cancellationToken).ConfigureAwait(false)
                            ? WebCommandResult.Handled
                            : WebCommandResult.Rejected;
                    case "copyNotice" when Text(root, "batchId") is { } copyNoticeId && _copyNotice is not null:
                        return await _copyNotice(copyNoticeId, cancellationToken).ConfigureAwait(false)
                            ? WebCommandResult.Handled
                            : WebCommandResult.Rejected;
                    case "copyComparison" when _copyComparison is not null:
                        return await _copyComparison(cancellationToken).ConfigureAwait(false)
                            ? WebCommandResult.Handled
                            : WebCommandResult.Rejected;
                    case "exportCalendar" when EventIds(root) is { } eventIds && _exportCalendar is not null:
                        return await _exportCalendar(eventIds, cancellationToken).ConfigureAwait(false)
                            ? WebCommandResult.Handled
                            : WebCommandResult.Rejected;
                    case "copySupportUrl":
                        _clipboard.SetText(ProductInfo.SupportUrl);
                        return WebCommandResult.Handled;
                    case "restartForUpdate":
                        return _restartForUpdate() ? WebCommandResult.Handled : WebCommandResult.Rejected;
                    case "openLogs" when _openLogs is not null:
                        _openLogs();
                        return WebCommandResult.Handled;
                    case "openExternal" when Text(root, "url") is { } url && TrustedUri(url) is { } uri:
                        _launcher.Open(uri);
                        return WebCommandResult.Handled;
                    default:
                        return WebCommandResult.Rejected;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                var message = FailureMessage(type);
                _logger?.Log(AppLogLevel.Error, "web-command", message, error);
                return new WebCommandResult(WebCommandKind.Failed, message);
            }
        }
    }

    private static string FailureMessage(string? type) => type switch
    {
        "copySupportUrl" => "Не удалось скопировать ссылку.",
        "openExternal" => "Не удалось открыть ссылку.",
        "openLogs" => "Не удалось открыть журнал.",
        "refresh" => "Не удалось обновить данные.",
        "openChanges" or "dismissNotice" => "Не удалось открыть изменения.",
        "setGroups" or "setTheme" or "setPublicHistory" or "saveProfile" or "skipProfile" => "Не удалось сохранить настройки.",
        "compareWith" => "Не удалось сравнить снимки.",
        "copyBatch" or "copyNotice" or "copyComparison" => "Не удалось скопировать сводку.",
        "exportCalendar" => "Не удалось экспортировать календарь.",
        "restartForUpdate" => "Не удалось запустить обновление.",
        _ => "Команда не выполнена."
    };

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;

    private static bool? Boolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var node) && node.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? node.GetBoolean()
            : null;

    private static List<string>? Groups(JsonElement element)
    {
        if (!element.TryGetProperty("groups", out var node) || node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > 200)
        {
            return null;
        }

        var groups = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: <= 200 } raw)
            {
                return null;
            }
            var value = GroupKey.Normalize(raw);
            if (value.Length == 0) return null;
            if (!groups.Contains(value, StringComparer.Ordinal)) groups.Add(value);
        }
        return groups;
    }

    private static List<string>? EventIds(JsonElement element)
    {
        if (!element.TryGetProperty("eventIds", out var node) || node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > 1000)
        {
            return null;
        }
        var ids = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 and <= 200 } id) return null;
            ids.Add(id);
        }
        return ids;
    }

    private static WebProfileSelection? Profile(JsonElement element)
    {
        var roles = Values(element, "roles", 3);
        var sectors = Values(element, "sectors", 50);
        var groups = Groups(element);
        if (roles is null || sectors is null || groups is null
            || roles.Any(role => role is not ("retail" or "producer" or "wholesale")))
        {
            return null;
        }
        return new WebProfileSelection(roles, sectors, groups);
    }

    private static List<string>? Values(JsonElement element, string property, int limit)
    {
        if (!element.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > limit)
        {
            return null;
        }
        var values = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: <= 100 } raw) return null;
            var value = raw.Trim().ToLowerInvariant();
            if (value.Length == 0) return null;
            if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        }
        return values;
    }

    private static string? Theme(JsonElement element)
    {
        var theme = Text(element, "theme");
        return theme is "auto" or "light" or "dark" ? theme : null;
    }

    private static Uri? TrustedUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var idnHost = uri.IdnHost;
        if (idnHost.Equals("xn--80ajghhoc2aj1c8b.xn--p1ai", StringComparison.OrdinalIgnoreCase)
            || idnHost.Equals("pay.cloudtips.ru", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        return idnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/jadieify-hub/marking-calendar", StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;
    }
}
