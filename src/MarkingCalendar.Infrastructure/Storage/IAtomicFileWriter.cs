namespace MarkingCalendar.Infrastructure.Storage;

public interface IAtomicFileWriter
{
    Task WriteJsonAsync<T>(string destination, T value, CancellationToken cancellationToken);
    Task WriteTextAsync(string destination, string value, CancellationToken cancellationToken);
}
