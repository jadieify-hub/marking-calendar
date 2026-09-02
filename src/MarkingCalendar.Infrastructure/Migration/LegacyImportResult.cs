namespace MarkingCalendar.Infrastructure.Migration;

public enum LegacyImportStatus
{
    Imported,
    AlreadyImported,
    NotFound,
    ExistingData,
    Rejected,
    Failed
}

public sealed record LegacyImportResult(LegacyImportStatus Status, string Message, Exception? DiagnosticError = null);

