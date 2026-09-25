using System.IO;
using System.Text.Json;
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
    private bool _pdfInProgress;

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

    public Task PrintCalendarAsync(bool savePdf, CancellationToken cancellationToken) =>
        UiDispatcher.InvokeAsync(Dispatcher, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var browser = Browser.CoreWebView2 ?? throw new InvalidOperationException("Интерфейс ещё не готов к печати.");
            if (_pdfInProgress) return;
            if (!savePdf)
            {
                browser.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                return;
            }

            _pdfInProgress = true;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    AddExtension = true,
                    DefaultExt = ".pdf",
                    Filter = "Документ PDF (*.pdf)|*.pdf",
                    FileName = $"Календарь маркировки-{DateTime.Today:yyyy-MM-dd}.pdf"
                };
                if (dialog.ShowDialog(this) != true) return;
                var temporary = $"{dialog.FileName}.{Guid.NewGuid():N}.tmp.pdf";
                try
                {
                    var settings = browser.Environment.CreatePrintSettings();
                    settings.Orientation = CoreWebView2PrintOrientation.Landscape;
                    settings.PageWidth = 210 / 25.4;
                    settings.PageHeight = 297 / 25.4;
                    settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 12 / 25.4;
                    settings.ShouldPrintHeaderAndFooter = false;
                    if (!await browser.PrintToPdfAsync(temporary, settings))
                        throw new IOException("WebView2 не смог создать PDF.");
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, dialog.FileName, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            finally { _pdfInProgress = false; }
        });

    public void PostOpenChanges(string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        Browser.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "openChanges", batchId }));
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
