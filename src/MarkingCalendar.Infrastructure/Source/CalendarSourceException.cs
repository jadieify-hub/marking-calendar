namespace MarkingCalendar.Infrastructure.Source;

public enum CalendarSourceError
{
    HttpFailure,
    InvalidPayload,
    NetworkFailure
}

public sealed class CalendarSourceException : Exception
{
    public CalendarSourceException() : this(CalendarSourceError.NetworkFailure, "Не удалось получить календарь.")
    {
    }

    public CalendarSourceException(string message) : this(CalendarSourceError.NetworkFailure, message)
    {
    }

    public CalendarSourceException(string message, Exception innerException)
        : this(CalendarSourceError.NetworkFailure, message, innerException)
    {
    }

    public CalendarSourceException(
        CalendarSourceError code,
        string message,
        Exception? innerException = null,
        string? rawJson = null)
        : base(message, innerException)
    {
        Code = code;
        RawJson = rawJson;
    }

    public CalendarSourceError Code { get; }
    public string? RawJson { get; }
}
