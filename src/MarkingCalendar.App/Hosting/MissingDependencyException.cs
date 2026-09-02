namespace MarkingCalendar.App.Hosting;

internal sealed class MissingDependencyException(
    string message,
    Uri downloadUri,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public Uri DownloadUri { get; } = downloadUri ?? throw new ArgumentNullException(nameof(downloadUri));
}

internal static class DependencyLinks
{
    public static Uri WebView2 { get; } = new("https://developer.microsoft.com/en-us/microsoft-edge/webview2/");
}
