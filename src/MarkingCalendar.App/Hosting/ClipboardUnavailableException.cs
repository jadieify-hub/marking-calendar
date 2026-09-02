namespace MarkingCalendar.App.Hosting;

public sealed class ClipboardUnavailableException : Exception
{
    public ClipboardUnavailableException() { }
    public ClipboardUnavailableException(string message) : base(message) { }
    public ClipboardUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
