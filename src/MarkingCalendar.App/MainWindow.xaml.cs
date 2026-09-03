using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.App.Web;
using MarkingCalendar.Infrastructure.Diagnostics;

namespace MarkingCalendar.App;

public partial class MainWindow : Window
{
    private WebMessageRouter? _router;
    private Uri? _dependencyDownloadUri;
    private readonly IAppLogger? _logger;
    private readonly string _browserDataDirectory;
    private Func<string, Task>? _reportCommandFailure;
    private string _titleBarTheme = "dark";

    public MainWindow(string browserDataDirectory, IAppLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserDataDirectory);
        _browserDataDirectory = Path.GetFullPath(browserDataDirectory);
        _logger = logger;
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, _titleBarTheme);
    }

    public void ApplyTitleBarTheme(string preference)
    {
        _titleBarTheme = preference;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ApplyTitleBarTheme(preference));
            return;
        }
        TitleBarTheme.Apply(this, preference);
    }

    public Task InitializeBrowserAsync(
        WebMessageRouter router,
        Func<string, Task> reportCommandFailure,
        CancellationToken cancellationToken)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _reportCommandFailure = reportCommandFailure ?? throw new ArgumentNullException(nameof(reportCommandFailure));
        return UiDispatcher.InvokeAsync(
            Dispatcher,
            () => InitializeBrowserCoreAsync(cancellationToken));
    }

    private async Task InitializeBrowserCoreAsync(CancellationToken cancellationToken)
    {
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new FileNotFoundException("Файлы интерфейса не найдены.", Path.Combine(webRoot, "index.html"));
        }

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _browserDataDirectory);
            await Browser.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException error)
        {
            throw new MissingDependencyException(
                "Не найден Microsoft Edge WebView2 Runtime.",
                DependencyLinks.WebView2,
                error);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.markingcalendar.local",
            webRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        Browser.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        Browser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        Browser.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        Browser.Source = new Uri("https://app.markingcalendar.local/index.html");
        Browser.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    public Task PostStateAsync(AppViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Browser.CoreWebView2 is null) return Task.CompletedTask;
        var json = WebStateSerializer.Serialize(model);
        Browser.CoreWebView2.PostWebMessageAsJson(json);
        return Task.CompletedTask;
    }

    public void ShowFatalError(string message, string details, Uri? downloadUri = null)
    {
        Browser.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorMessage.Text = message;
        ErrorDetails.Text = details;
        _dependencyDownloadUri = downloadUri;
        DependencyDownloadButton.Visibility = downloadUri is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_router is null) return;
        try
        {
            var result = await _router.HandleAsync(e.WebMessageAsJson, CancellationToken.None);
            if (result.Kind == WebCommandKind.Failed && result.Message is not null)
            {
                await ReportCommandFailureSafelyAsync(result.Message);
            }
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "web-command", "Команда интерфейса не выполнена.", error);
            await ReportCommandFailureSafelyAsync("Команда не выполнена.");
        }
    }

    private async Task ReportCommandFailureSafelyAsync(string message)
    {
        if (_reportCommandFailure is null) return;
        try
        {
            await _reportCommandFailure(message);
        }
        catch (Exception error)
        {
            _logger?.Log(AppLogLevel.Error, "web-command", "Не удалось показать сообщение об ошибке команды.", error);
        }
    }

    private static void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private static void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("app.markingcalendar.local", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void DependencyDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dependencyDownloadUri is not null)
        {
            new ShellExternalLauncher().Open(_dependencyDownloadUri);
        }
    }
}
