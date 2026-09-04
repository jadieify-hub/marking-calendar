using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MarkingCalendar.App.Hosting;

public sealed class ChangeNotificationService : IDisposable
{
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private Action? _clicked;

    public ChangeNotificationService()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MarkingCalendar.Resources.calendar.ico")
            ?? throw new FileNotFoundException("Иконка приложения не найдена.");
        using var sourceIcon = new Icon(stream);
        _icon = (Icon)sourceIcon.Clone();
        _notifyIcon = new NotifyIcon { Icon = _icon, Text = ProductInfo.Name };
        _notifyIcon.BalloonTipClicked += NotificationClicked;
        _notifyIcon.BalloonTipClosed += NotificationClosed;
    }

    public void Show(int changeCount, Action clicked)
    {
        _clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(10_000, ProductInfo.Name, $"Найдены изменения: {changeCount}", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.BalloonTipClicked -= NotificationClicked;
        _notifyIcon.BalloonTipClosed -= NotificationClosed;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void NotificationClicked(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        var clicked = _clicked;
        _clicked = null;
        clicked?.Invoke();
    }

    private void NotificationClosed(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _clicked = null;
    }
}
