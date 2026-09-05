using System.Reflection;

namespace MarkingCalendar.App;

public static class ProductInfo
{
    public const string Name = "Календарь маркировки";
    public static string Version { get; } =
        typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? "0.1.6";
    public const string Developer = "Руслан Керусов";
    public const string Publisher = "KRS";
    public const string RepositoryUrl = "https://github.com/jadieify-hub/marking-calendar";
    public const string UpdateFeedUrl = "https://raw.githubusercontent.com/jadieify-hub/marking-calendar/releases/";
    public const string PublicHistoryUrl = "https://github.com/jadieify-hub/marking-calendar/blob/data/CHANGELOG.md";
    public const string SupportUrl = "https://pay.cloudtips.ru/p/a18da555";
    public const string PublicHistoryManifestUrl = "https://raw.githubusercontent.com/jadieify-hub/marking-calendar/data/manifest.json";
    public const string Disclaimer = "Независимый проект, не являющийся официальным приложением оператора системы маркировки.";
}
