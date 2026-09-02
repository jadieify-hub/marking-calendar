using Velopack;

namespace MarkingCalendar.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();
        var application = new App();
        application.Run();
    }
}
