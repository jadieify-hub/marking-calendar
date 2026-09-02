using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Events;

public sealed class GroupKeyTests
{
    [Theory]
    [InlineData(" Радиоэлектроника\u00a0 ", "радиоэлектроника")]
    [InlineData("Антисептики   и ёмкости", "антисептики и емкости")]
    public void Normalize_ProducesStableStorageKey(string value, string expected)
    {
        Assert.Equal(expected, GroupKey.Normalize(value));
    }
}
