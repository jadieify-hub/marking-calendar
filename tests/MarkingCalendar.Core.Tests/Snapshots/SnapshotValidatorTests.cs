using MarkingCalendar.Core.Events;
using MarkingCalendar.Core.Snapshots;

namespace MarkingCalendar.Core.Tests.Snapshots;

public sealed class SnapshotValidatorTests
{
    [Fact]
    public void Validate_RejectsEmptySnapshot()
    {
        var result = new SnapshotValidator().Validate(Snapshot([]), null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SnapshotValidationErrorCode.Empty);
    }

    [Fact]
    public void Validate_RejectsDuplicateEventIds()
    {
        var item = Event(1);

        var result = new SnapshotValidator().Validate(Snapshot([item, item]), null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SnapshotValidationErrorCode.DuplicateId);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredEventField()
    {
        var invalid = Event(1) with { Stage = "" };

        var result = new SnapshotValidator().Validate(Snapshot([invalid]), null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SnapshotValidationErrorCode.RequiredField);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(110)]
    [InlineData(215)]
    public void Validate_RejectsDropBelowHalfOfBaseline(int count)
    {
        var baseline = Snapshot(Enumerable.Range(1, 432).Select(Event).ToArray());
        var candidate = Snapshot(Enumerable.Range(1, count).Select(Event).ToArray());

        var result = new SnapshotValidator().Validate(candidate, baseline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == SnapshotValidationErrorCode.AnomalousCount);
    }

    [Theory]
    [InlineData(216)]
    [InlineData(400)]
    public void Validate_AcceptsAtLeastHalfOfBaseline(int count)
    {
        var baseline = Snapshot(Enumerable.Range(1, 432).Select(Event).ToArray());
        var candidate = Snapshot(Enumerable.Range(1, count).Select(Event).ToArray());

        var result = new SnapshotValidator().Validate(candidate, baseline);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_AcceptsNonEmptyBundledSnapshotWithoutBaseline()
    {
        var result = new SnapshotValidator().Validate(Snapshot([Event(1)]), null);

        Assert.True(result.IsValid);
    }

    private static CalendarSnapshot Snapshot(IReadOnlyList<CalendarEvent> events) =>
        CalendarSnapshot.Create(new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), new Uri("https://example.test/source"), events);

    private static CalendarEvent Event(int index)
    {
        var date = new DateOnly(2026, 1, 1).AddDays(index);
        return new CalendarEvent($"event-{index}", date, null, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), $"Группа {index}", "Маркировка", "Старт", "Описание", null);
    }
}
