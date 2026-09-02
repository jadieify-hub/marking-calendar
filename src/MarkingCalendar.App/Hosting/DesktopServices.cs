using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using MarkingCalendar.App.Web;

namespace MarkingCalendar.App.Hosting;

internal sealed class ShellExternalLauncher : IExternalLauncher
{
    public void Open(Uri uri)
    {
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}

public sealed class WpfClipboardService(
    Action<string>? setText = null,
    Action<TimeSpan>? delay = null) : IClipboardService
{
    private readonly Action<string> _setText = setText ?? Clipboard.SetText;
    private readonly Action<TimeSpan> _delay = delay ?? Thread.Sleep;

    public void SetText(string value)
    {
        ExternalException? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _setText(value);
                return;
            }
            catch (ExternalException error)
            {
                lastError = error;
                if (attempt < 2) _delay(TimeSpan.FromMilliseconds(50));
            }
        }

        throw new ClipboardUnavailableException("Буфер обмена временно недоступен.", lastError!);
    }
}

internal sealed class ShellFolderLauncher
{
    public static void Open(string path)
    {
        Directory.CreateDirectory(path);
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
