using MarkingCalendar.App.Hosting;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Hosting;

public sealed class WindowPlacementPolicyTests
{
    [Fact]
    public void CreateInitial_UsesThreeQuartersOfTheAvailableWidthAndCentersTheWindow()
    {
        var resolved = WindowPlacementPolicy.CreateInitial(
            new DesktopBounds(0, 0, 2560, 1440),
            minimumWidth: 900,
            minimumHeight: 640,
            preferredHeight: 900);

        Assert.Equal(new WindowPlacementState(320, 270, 1920, 900, false), resolved);
    }

    [Theory]
    [InlineData(120, 80, 1680, 950, 120, 80, 1680, 950)]
    [InlineData(-4000, -2000, 3000, 2000, 0, 0, 1920, 1080)]
    public void Resolve_KeepsTheRestoredWindowInsideTheAvailableDesktop(
        double left,
        double top,
        double width,
        double height,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedHeight)
    {
        var saved = new WindowPlacementState(left, top, width, height, true);

        var resolved = WindowPlacementPolicy.Resolve(
            saved,
            new DesktopBounds(0, 0, 1920, 1080),
            minimumWidth: 900,
            minimumHeight: 640);

        Assert.Equal(new WindowPlacementState(
            expectedLeft,
            expectedTop,
            expectedWidth,
            expectedHeight,
            true), resolved);
    }
}
