using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Windows;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Application releases the bootstrapper in OnExit.")]
public partial class App : System.Windows.Application
{
    private AppBootstrapper? _bootstrapper;
    private FileAppLogger? _logger;
    private WindowPlacementStore? _windowPlacementStore;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.ForCurrentUser();
        _logger = new FileAppLogger(paths, TimeProvider.System);
        RegisterUnhandledErrorLogging();
        _logger.Log(AppLogLevel.Info, "startup", $"Запуск приложения {ProductInfo.Version}.");
        var writer = new AtomicFileWriter();
        _windowPlacementStore = new WindowPlacementStore(paths, writer);
        WindowPlacementState? placement = null;
        try
        {
            placement = await _windowPlacementStore.LoadAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Log(AppLogLevel.Warning, "window", "Не удалось восстановить положение окна.", error);
        }
        var window = new MainWindow(paths.BrowserDataDirectory, _logger);
        MainWindow = window;
        WindowPlacementController.Restore(window, placement);
        window.Closing += MainWindow_Closing;
        window.Show();
        _bootstrapper = new AppBootstrapper(window, _logger);
        try
        {
            await _bootstrapper.InitializeAsync(CancellationToken.None);
        }
        catch (MissingDependencyException error)
        {
            _logger.Log(AppLogLevel.Error, "startup", "Не найден обязательный компонент Microsoft.", error);
            window.ShowFatalError(
                "Для запуска не хватает компонента Microsoft.",
                error.Message,
                error.DownloadUri);
        }
        catch (Exception error)
        {
            _logger.Log(AppLogLevel.Error, "startup", "Приложение не удалось запустить.", error);
            window.ShowFatalError("Приложение не удалось запустить. Переустановите его или откройте раздел Issues на GitHub.", error.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Log(AppLogLevel.Info, "shutdown", "Приложение завершает работу.");
        _bootstrapper?.Dispose();
        UnregisterUnhandledErrorLogging();
        base.OnExit(e);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || sender is not Window window || _windowPlacementStore is null) return;
        try
        {
            _windowPlacementStore
                .SaveAsync(WindowPlacementController.Capture(window), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.Log(AppLogLevel.Warning, "window", "Не удалось сохранить положение окна.", error);
        }
    }

    private void RegisterUnhandledErrorLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void UnregisterUnhandledErrorLogging()
    {
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.Log(
            AppLogLevel.Error,
            "unhandled",
            e.IsTerminating ? "Необработанная ошибка завершает процесс." : "Необработанная ошибка AppDomain.",
            e.ExceptionObject as Exception);

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Log(AppLogLevel.Error, "unhandled-task", "Необработанная ошибка фоновой задачи.", e.Exception);
        e.SetObserved();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        _logger?.Log(AppLogLevel.Error, "unhandled-ui", "Необработанная ошибка интерфейса.", e.Exception);
}
