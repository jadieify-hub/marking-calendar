namespace MarkingCalendar.Core.Snapshots;

public sealed class SnapshotValidator(SnapshotValidationOptions? options = null) : ISnapshotValidator
{
    private readonly SnapshotValidationOptions _options = options ?? new SnapshotValidationOptions();

    public SnapshotValidationResult Validate(CalendarSnapshot candidate, CalendarSnapshot? baseline)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var errors = new List<SnapshotValidationError>();

        if (candidate.Events.Count == 0)
        {
            errors.Add(new SnapshotValidationError(SnapshotValidationErrorCode.Empty, "Источник вернул пустой календарь."));
            return new SnapshotValidationResult(errors);
        }

        foreach (var item in candidate.Events)
        {
            if (string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.Group)
                || string.IsNullOrWhiteSpace(item.Type)
                || string.IsNullOrWhiteSpace(item.Stage))
            {
                errors.Add(new SnapshotValidationError(
                    SnapshotValidationErrorCode.RequiredField,
                    "Одно или несколько событий не содержат обязательные поля."));
                break;
            }

            if (!DateInRange(item.Start) || !DateInRange(item.End) || item.Start > item.End)
            {
                errors.Add(new SnapshotValidationError(
                    SnapshotValidationErrorCode.InvalidDateRange,
                    $"Событие «{item.Group} — {item.Type}» содержит недопустимый диапазон дат."));
                break;
            }
        }

        if (candidate.Events.GroupBy(item => item.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            errors.Add(new SnapshotValidationError(
                SnapshotValidationErrorCode.DuplicateId,
                "Источник вернул события с повторяющимися идентификаторами."));
        }

        if (baseline is not null
            && baseline.Events.Count >= _options.MinimumExpectedEvents
            && candidate.Events.Count < _options.MinimumExpectedEvents
            && candidate.Events.Count < baseline.Events.Count * _options.MinimumBaselineRatio)
        {
            errors.Add(new SnapshotValidationError(
                SnapshotValidationErrorCode.AnomalousCount,
                $"Количество событий аномально уменьшилось: {baseline.Events.Count} → {candidate.Events.Count}."));
        }

        return errors.Count == 0 ? SnapshotValidationResult.Valid : new SnapshotValidationResult(errors);
    }

    private bool DateInRange(DateOnly? value) =>
        value is null || value.Value.Year >= _options.EarliestYear && value.Value.Year <= _options.LatestYear;
}
