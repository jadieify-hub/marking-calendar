using MarkingCalendar.Core.Events;

namespace MarkingCalendar.Core.Tests.Events;

public sealed class EventIdTests
{
    [Fact]
    public void FromCanonicalContent_PreservesLowercaseSha256Format()
    {
        var id = EventId.FromCanonicalContent("test");

        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", id);
    }
}
