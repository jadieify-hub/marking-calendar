using MarkingCalendar.App.Hosting;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class TitleBarThemeTests
{
    [Theory]
    [InlineData("dark", true, true)]
    [InlineData("dark", false, true)]
    [InlineData("light", true, false)]
    [InlineData("light", false, false)]
    [InlineData("auto", true, false)]
    [InlineData("auto", false, true)]
    public void Resolve_UsesExplicitPreferenceOrWindowsAppTheme(
        string preference,
        bool appsUseLightTheme,
        bool expectedDark)
    {
        var palette = TitleBarTheme.Resolve(preference, appsUseLightTheme);

        Assert.Equal(expectedDark, palette.IsDark);
    }
}
