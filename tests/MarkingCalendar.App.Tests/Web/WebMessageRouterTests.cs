using MarkingCalendar.App.Hosting;
using MarkingCalendar.App.Web;
using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.App.Tests.Web;

public sealed class WebMessageRouterTests
{
    [Theory]
    [InlineData("https://честныйзнак.рф/business/projects/beer/")]
    [InlineData("https://github.com/jadieify-hub/marking-calendar")]
    [InlineData("https://pay.cloudtips.ru/p/53698013")]
    public async Task HandleAsync_OpensOnlyTrustedHttpsTargets(string url)
    {
        var launcher = new RecordingLauncher();
        var router = Router(launcher, new RecordingClipboard());

        var result = await router.HandleAsync($$"""{"type":"openExternal","url":"{{url}}"}""", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, result);
        Assert.Equal(url.TrimEnd('/'), Assert.Single(launcher.Opened).AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("https://evil.example/phishing")]
    public async Task HandleAsync_RejectsUnsafeExternalTargets(string url)
    {
        var launcher = new RecordingLauncher();
        var router = Router(launcher, new RecordingClipboard());

        var result = await router.HandleAsync($$"""{"type":"openExternal","url":"{{url}}"}""", CancellationToken.None);

        Assert.Equal(WebCommandResult.Rejected, result);
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public async Task HandleAsync_CopiesSingleConfiguredSupportUrl()
    {
        var clipboard = new RecordingClipboard();
        var router = Router(new RecordingLauncher(), clipboard);

        var result = await router.HandleAsync("{\"type\":\"copySupportUrl\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, result);
        Assert.Equal("https://pay.cloudtips.ru/p/53698013", Assert.Single(clipboard.Values));
    }

    [Fact]
    public async Task HandleAsync_RejectsUnknownCommand()
    {
        var result = await Router(new RecordingLauncher(), new RecordingClipboard())
            .HandleAsync("{\"type\":\"runPowerShell\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Rejected, result);
    }

    [Fact]
    public async Task HandleAsync_RestartsOnlyThroughConfiguredUpdateAction()
    {
        var called = false;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            restartForUpdate: () =>
            {
                called = true;
                return true;
            });

        var result = await router.HandleAsync("{\"type\":\"restartForUpdate\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, result);
        Assert.True(called);
    }

    [Fact]
    public async Task HandleAsync_OpensLocalLogDirectoryOnlyThroughConfiguredAction()
    {
        var called = false;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            openLogs: () => called = true);

        var result = await router.HandleAsync("{\"type\":\"openLogs\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, result);
        Assert.True(called);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailedAndLogsWhenCommandThrows()
    {
        var logger = new RecordingLogger();
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new ThrowingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            logger: logger);

        var result = await router.HandleAsync("{\"type\":\"copySupportUrl\"}", CancellationToken.None);

        Assert.Equal(WebCommandKind.Failed, result.Kind);
        Assert.Equal("Не удалось скопировать ссылку.", result.Message);
        var logged = Assert.Single(logger.Entries);
        Assert.Equal("web-command", logged.Source);
        Assert.IsType<ClipboardUnavailableException>(logged.Exception);
    }

    [Fact]
    public async Task HandleAsync_MarksHistorySeenThroughConfiguredAction()
    {
        var called = false;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            markHistorySeen: _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        var result = await router.HandleAsync("{\"type\":\"markHistorySeen\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, result);
        Assert.True(called);
    }

    [Fact]
    public async Task HandleAsync_SavesGroupsAndThemeThroughPreferenceHandlers()
    {
        IReadOnlyList<string>? groups = null;
        string? theme = null;
        bool? publicHistoryEnabled = null;
        string? hiddenGroup = null;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            preferences: new WebPreferenceHandlers(
                (value, _) => { groups = value; return Task.CompletedTask; },
                (value, _) => { theme = value; return Task.CompletedTask; },
                (value, _) => { publicHistoryEnabled = value; return Task.CompletedTask; },
                (value, _) => { hiddenGroup = value; return Task.CompletedTask; }));

        var groupsResult = await router.HandleAsync("{\"type\":\"setGroups\",\"groups\":[\" Обувь \",\"Игрушки\",\"Обувь\"]}", CancellationToken.None);
        var themeResult = await router.HandleAsync("{\"type\":\"setTheme\",\"theme\":\"dark\"}", CancellationToken.None);
        var historyResult = await router.HandleAsync("{\"type\":\"setPublicHistory\",\"enabled\":false}", CancellationToken.None);
        var hideResult = await router.HandleAsync("{\"type\":\"hideGroupSuggestion\",\"key\":\" Игрушки \"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, groupsResult);
        Assert.Equal(["обувь", "игрушки"], groups);
        Assert.Equal(WebCommandResult.Handled, themeResult);
        Assert.Equal("dark", theme);
        Assert.Equal(WebCommandResult.Handled, historyResult);
        Assert.False(publicHistoryEnabled);
        Assert.Equal(WebCommandResult.Handled, hideResult);
        Assert.Equal("игрушки", hiddenGroup);
    }

    [Fact]
    public async Task HandleAsync_SavesOrSkipsProfileThroughPreferenceHandlers()
    {
        WebProfileSelection? profile = null;
        var skipped = false;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            preferences: new WebPreferenceHandlers(
                (_, _) => Task.CompletedTask,
                (_, _) => Task.CompletedTask,
                SaveProfile: (value, _) => { profile = value; return Task.CompletedTask; },
                SkipProfile: _ => { skipped = true; return Task.CompletedTask; }));

        var saved = await router.HandleAsync(
            "{\"type\":\"saveProfile\",\"roles\":[\"retail\",\"producer\"],\"sectors\":[\"food\",\"pharma\"],\"groups\":[\" БАД \"]}",
            CancellationToken.None);
        var skippedResult = await router.HandleAsync("{\"type\":\"skipProfile\"}", CancellationToken.None);

        Assert.Equal(WebCommandResult.Handled, saved);
        Assert.Equal(["retail", "producer"], profile?.Roles);
        Assert.Equal(["food", "pharma"], profile?.Sectors);
        Assert.Equal(["бад"], profile?.Groups);
        Assert.Equal(WebCommandResult.Handled, skippedResult);
        Assert.True(skipped);
    }

    [Theory]
    [InlineData("{\"type\":\"setGroups\",\"groups\":\"Обувь\"}")]
    [InlineData("{\"type\":\"setTheme\",\"theme\":\"neon\"}")]
    [InlineData("{\"type\":\"setPublicHistory\",\"enabled\":\"no\"}")]
    [InlineData("{\"type\":\"saveProfile\",\"roles\":\"retail\",\"sectors\":[],\"groups\":[]}")]
    public async Task HandleAsync_RejectsInvalidPreferences(string json)
    {
        var router = Router(new RecordingLauncher(), new RecordingClipboard());

        var result = await router.HandleAsync(json, CancellationToken.None);

        Assert.Equal(WebCommandResult.Rejected, result);
    }

    [Fact]
    public async Task HandleAsync_RejectsUnknownArchiveComparisonId()
    {
        var requested = string.Empty;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            compareWith: (id, _) =>
            {
                requested = id;
                return Task.FromResult(false);
            });

        var result = await router.HandleAsync("{\"type\":\"compareWith\",\"id\":\"unknown\"}", CancellationToken.None);

        Assert.Equal("unknown", requested);
        Assert.Equal(WebCommandResult.Rejected, result);
    }

    [Fact]
    public async Task HandleAsync_RejectsUnknownBatchWhenCopyingSummary()
    {
        var requested = string.Empty;
        var router = new WebMessageRouter(
            new RecordingLauncher(),
            new RecordingClipboard(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            copyBatch: (id, _) =>
            {
                requested = id;
                return Task.FromResult(false);
            });

        var result = await router.HandleAsync("{\"type\":\"copyBatch\",\"batchId\":\"missing\"}", CancellationToken.None);

        Assert.Equal("missing", requested);
        Assert.Equal(WebCommandResult.Rejected, result);
    }

    private static WebMessageRouter Router(IExternalLauncher launcher, IClipboardService clipboard) =>
        new(launcher, clipboard, _ => Task.CompletedTask, _ => Task.CompletedTask, (_, _) => Task.CompletedTask);

    private sealed class RecordingLauncher : IExternalLauncher
    {
        public List<Uri> Opened { get; } = [];
        public void Open(Uri uri) => Opened.Add(uri);
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public List<string> Values { get; } = [];
        public void SetText(string value) => Values.Add(value);
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public void SetText(string value) => throw new ClipboardUnavailableException("Буфер обмена недоступен.");
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<(string Source, Exception? Exception)> Entries { get; } = [];

        public void Log(AppLogLevel level, string source, string message, Exception? exception = null) =>
            Entries.Add((source, exception));

        public Task SaveRejectedJsonAsync(string source, string json, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
