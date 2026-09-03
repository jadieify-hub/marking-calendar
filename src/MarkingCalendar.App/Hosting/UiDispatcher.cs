using System.Windows.Threading;

namespace MarkingCalendar.App.Hosting;

public static class UiDispatcher
{
    public static Task InvokeAsync(Dispatcher dispatcher, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(operation);

        return dispatcher.CheckAccess()
            ? operation()
            : dispatcher.InvokeAsync(operation).Task.Unwrap();
    }
}
