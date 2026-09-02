using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MarkingCalendar.App.Hosting;

public readonly record struct TitleBarPalette(bool IsDark, int CaptionColor, int TextColor);

public static class TitleBarTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    private static readonly TitleBarPalette Dark = new(
        true,
        ColorRef(0x0f, 0x12, 0x17),
        ColorRef(0xe9, 0xed, 0xf3));

    private static readonly TitleBarPalette Light = new(
        false,
        ColorRef(0xee, 0xf0, 0xf4),
        ColorRef(0x15, 0x19, 0x20));

    public static TitleBarPalette Resolve(string preference, bool appsUseLightTheme) =>
        preference switch
        {
            "dark" => Dark,
            "light" => Light,
            _ => appsUseLightTheme ? Light : Dark
        };

    public static void Apply(Window window, string preference)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var palette = Resolve(preference, AppsUseLightTheme());
        var darkMode = palette.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref darkMode, sizeof(int)) < 0)
        {
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref darkMode, sizeof(int));
        }

        var captionColor = palette.CaptionColor;
        var textColor = palette.TextColor;
        _ = DwmSetWindowAttribute(handle, BorderColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(handle, TextColor, ref textColor, sizeof(int));
    }

    private static bool AppsUseLightTheme()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1) is not int value || value != 0;
        }
        catch (SystemException)
        {
            return true;
        }
    }

    private static int ColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
