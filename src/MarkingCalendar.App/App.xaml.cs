using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.Infrastructure.Diagnostics;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Application releases the bootstrapper in OnExit.")]
public partial class App : Application
{
    private AppBootstrapper? _bootstrapper;
    private FileAppLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.ForCurrentUser();
        _logger = new FileAppLogger(paths, TimeProvider.System);
        RegisterUnhandledErrorLogging();
        _logger.Log(AppLogLevel.Info, "startup", $"Запуск приложения {ProductInfo.Version}.");
        var window = new MainWindow(_logger);
        MainWindow = window;
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
