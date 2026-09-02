using System.Runtime.InteropServices;
using MarkingCalendar.App.Hosting;
using MarkingCalendar.App.Web;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class DesktopServicesTests
{
    [Fact]
    public void SetText_RetriesClipboardThreeTimesBeforeReportingUnavailable()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var service = new WpfClipboardService(
            _ =>
            {
                attempts++;
                throw new ClipboardBusyException();
            },
            delays.Add);

        var error = Assert.Throws<ClipboardUnavailableException>(() => service.SetText("value"));

        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)], delays);
        Assert.IsType<ClipboardBusyException>(error.InnerException);
    }

    [Fact]
    public void SetText_StopsRetryingAfterSuccessfulAttempt()
    {
        var attempts = 0;
        var service = new WpfClipboardService(
            _ =>
            {
                attempts++;
                if (attempts < 3) throw new ClipboardBusyException();
            },
            _ => { });

        service.SetText("value");

        Assert.Equal(3, attempts);
    }

    private sealed class ClipboardBusyException : ExternalException;
}
