using System.Windows;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Hosting;

public readonly record struct DesktopBounds(double Left, double Top, double Width, double Height);

public static class WindowPlacementPolicy
{
    public static WindowPlacementState? Resolve(
        WindowPlacementState? saved,
        DesktopBounds available,
        double minimumWidth,
        double minimumHeight)
    {
        if (saved is null
            || !IsFinite(saved.Left, saved.Top, saved.Width, saved.Height)
            || !IsFinite(available.Left, available.Top, available.Width, available.Height)
            || available.Width <= 0
            || available.Height <= 0)
        {
            return null;
        }

        var width = Math.Min(Math.Max(saved.Width, minimumWidth), available.Width);
        var height = Math.Min(Math.Max(saved.Height, minimumHeight), available.Height);
        var left = Math.Clamp(saved.Left, available.Left, available.Left + available.Width - width);
        var top = Math.Clamp(saved.Top, available.Top, available.Top + available.Height - height);
        return new WindowPlacementState(left, top, width, height, saved.Maximized);
    }

    private static bool IsFinite(params double[] values) => values.All(double.IsFinite);
}

internal static class WindowPlacementController
{
    public static void Restore(Window window, WindowPlacementState? saved)
    {
        ArgumentNullException.ThrowIfNull(window);
        var resolved = WindowPlacementPolicy.Resolve(
            saved,
            new DesktopBounds(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight),
            window.MinWidth,
            window.MinHeight);
        if (resolved is null) return;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = resolved.Left;
        window.Top = resolved.Top;
        window.Width = resolved.Width;
        window.Height = resolved.Height;
        if (resolved.Maximized) window.WindowState = WindowState.Maximized;
    }

    public static WindowPlacementState Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        return new WindowPlacementState(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            window.WindowState == WindowState.Maximized);
    }
}
