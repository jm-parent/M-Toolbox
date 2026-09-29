using System.Runtime.Versioning;
using Avalonia;
using Velopack;

[assembly: SupportedOSPlatform("windows5.1.2600")]

namespace MToolbox.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Doit s'exécuter avant tout : traite les hooks d'installation/mise à jour Velopack.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
