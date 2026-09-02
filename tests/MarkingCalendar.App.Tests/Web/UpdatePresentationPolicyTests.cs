using MarkingCalendar.App.Web;
using MarkingCalendar.Core.Changes;
using MarkingCalendar.Core.Events;
using MarkingCalendar.Infrastructure.Storage;

namespace MarkingCalendar.App.Tests.Web;

public sealed class UpdatePresentationPolicyTests
{
    [Fact]
    public void Evaluate_KeepsModalWhenNoGroupsAreSelected()
    {
        var batch = Batch(Event("Игрушки"));

        var result = Policy().Evaluate(batch, AppState.Initial);

        Assert.Same(batch, result.Notice);
        Assert.Null(result.Toast);
        Assert.False(result.MarkSeen);
    }

    [Fact]
    public void Evaluate_KeepsModalWhenSelectedGroupsHaveChanges()
    {
        var batch = Batch(Event("Игрушки"), Event("Обувь"));

        var result = Policy().Evaluate(batch, AppState.Initial.WithGroups(["Игрушки"]));

        Assert.Same(batch, result.Notice);
        Assert.Null(result.Toast);
        Assert.False(result.MarkSeen);
    }

    [Fact]
    public void Evaluate_UsesActionableToastAndMarksBatchSeenForOtherGroupsOnly()
    {
        var batch = Batch(Event("Обувь"));

        var result = Policy().Evaluate(batch, AppState.Initial.WithGroups(["Игрушки"]));

        Assert.Null(result.Notice);
        Assert.True(result.MarkSeen);
        Assert.Equal(
            new ToastViewModel("success", "Обновлено: 1 изменение по другим группам", "openChanges", batch.Id),
            result.Toast);
    }

    [Fact]
    public void Evaluate_KeepsGroupOnlyChangesInTheUpdateNotice()
    {
        var batch = new ChangeBatch(
            "groups",
            new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)),
            new ChangeSet([], [], [], [], groupsAdded: [new GroupChange("Новая группа", 2, new DateOnly(2027, 1, 1))]));

        var result = Policy().Evaluate(batch, AppState.Initial.WithGroups(["Игрушки"]));

        Assert.Same(batch, result.Notice);
        Assert.Null(result.Toast);
        Assert.False(result.MarkSeen);
    }

    private static UpdatePresentationPolicy Policy() =>
        new(new ChangeSummaryFactory(), new FixedTimeProvider());

    private static ChangeBatch Batch(params CalendarEvent[] events) =>
        new("batch", new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.FromHours(3)), new ChangeSet(events, [], [], []));

    private static CalendarEvent Event(string group) => new(
        EventId.FromCanonicalContent(group),
        new DateOnly(2026, 10, 1),
        null,
        "с 1 октября",
        group,
        "Розничная продажа",
        "Старт",
        string.Empty,
        null);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
    }
}
