using Avalonia.Controls;
using Avalonia.Input.Platform;
namespace MToolbox.App.Services;

public interface IPlatformService
{
    Task OpenUrlAsync(string url);
    Task CopyToClipboardAsync(string text);
}

public sealed class AvaloniaPlatformService(Func<TopLevel?> topLevel) : IPlatformService
{
    public async Task OpenUrlAsync(string url)
    {
        // Seuls les liens web sont ouverts : le catalogue ne doit pas pouvoir lancer un exécutable.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            return;

        if (topLevel() is { } tl) await tl.Launcher.LaunchUriAsync(uri);
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (topLevel()?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
