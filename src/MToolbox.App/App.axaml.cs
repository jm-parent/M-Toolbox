using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MToolbox.App.Services;
using MToolbox.App.ViewModels;
using MToolbox.App.Views;
using MToolbox.Core.Catalogue;

namespace MToolbox.App;

public partial class App : Application
{
    private const string RepoUrl = "https://github.com/jm-parent/M-Toolbox";
    private const string DefaultCatalogueUrl = "https://raw.githubusercontent.com/jm-parent/M-Toolbox/main/catalogue.json";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var platform = new AvaloniaPlatformService(() => TopLevel.GetTopLevel(window));

            // MTOOLBOX_CATALOGUE permet de pointer vers un fichier local ou une autre URL en développement.
            var source = Environment.GetEnvironmentVariable("MTOOLBOX_CATALOGUE") ?? DefaultCatalogueUrl;
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MToolbox", "cache");

            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MToolbox");

            var provider = new CatalogueProvider(
                Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri : new Uri(Path.GetFullPath(source)),
                cacheDir, http);

            var version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
            window.DataContext = new MainViewModel(provider, platform, new UpdateService(RepoUrl), version);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
