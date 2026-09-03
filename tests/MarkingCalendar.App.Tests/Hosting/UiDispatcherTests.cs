using System.Windows.Threading;
using MarkingCalendar.App.Hosting;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class UiDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_FromBackgroundThread_RunsOperationOnOwningStaThread()
    {
        var dispatcherReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherThread = new Thread(() =>
        {
            dispatcherReady.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        dispatcherThread.SetApartmentState(ApartmentState.STA);
        dispatcherThread.Start();

        var dispatcher = await dispatcherReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int? operationThreadId = null;
        ApartmentState? operationApartment = null;
        try
        {
            await Task.Run(() => UiDispatcher.InvokeAsync(dispatcher, () =>
            {
                operationThreadId = Environment.CurrentManagedThreadId;
                operationApartment = Thread.CurrentThread.GetApartmentState();
                return Task.CompletedTask;
            }));

            Assert.Equal(dispatcherThread.ManagedThreadId, operationThreadId);
            Assert.Equal(ApartmentState.STA, operationApartment);
        }
        finally
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(dispatcherThread.Join(TimeSpan.FromSeconds(5)));
        }
    }
}
