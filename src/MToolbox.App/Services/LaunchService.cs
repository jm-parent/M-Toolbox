using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.App.Services;

public interface ILaunchService
{
    /// <summary>Retourne un message à afficher à l'utilisateur.</summary>
    Task<string> LaunchAsync(Project project);
}

public sealed class LaunchService(
    IPlatformService platform,
    IEnrichmentService enrichment,
    ITokenStore tokens,
    HttpClient http,
    string downloadDirectory) : ILaunchService
{
    public async Task<string> LaunchAsync(Project project)
    {
        if (project.Launch is not { } spec) return "Aucune action de lancement pour ce projet.";

        try
        {
            return spec.Kind switch
            {
                LaunchKind.Url => await OpenUrlAsync(spec.Value),
                LaunchKind.Command => await CopyCommandAsync(spec.Value),
                LaunchKind.Path => await RunPathAsync(spec.Value),
                LaunchKind.GitHubRelease => await InstallReleaseAsync(project, spec.Value),
                _ => "Type de lancement inconnu.",
            };
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or Win32Exception
                                       or UnauthorizedAccessException or TaskCanceledException)
        {
            return $"Échec : {ex.Message}";
        }
    }

    private async Task<string> OpenUrlAsync(string? url)
    {
        if (!LaunchPolicy.IsWebUrl(url)) return "Lien invalide dans le catalogue.";
        await platform.OpenUrlAsync(url!);
        return "Ouverture dans le navigateur…";
    }

    private async Task<string> CopyCommandAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "Commande vide dans le catalogue.";
        // Jamais exécutée par l'application : l'utilisateur la colle lui-même dans son terminal.
        await platform.CopyToClipboardAsync(command);
        return "Commande copiée dans le presse-papiers.";
    }

    private async Task<string> RunPathAsync(string? path)
    {
        if (!LaunchPolicy.IsAllowedPath(path)) return "Chemin non autorisé (chemin absolu vers un exécutable, un script ou un dossier).";
        if (!File.Exists(path) && !Directory.Exists(path)) return $"Introuvable : {path}";

        if (!await platform.ConfirmAsync("Lancer le programme", $"Exécuter :\n{path}\n\nVérifiez que vous faites confiance à cet emplacement.")) return "Annulé.";

        Process.Start(new ProcessStartInfo(path!) { UseShellExecute = true });
        return "Lancé.";
    }

    private async Task<string> InstallReleaseAsync(Project project, string? pattern)
    {
        var info = await enrichment.GetAsync(project);
        if (info is null) return "Ce projet n'a pas de source GitHub exploitable.";
        if (info.Error is not null) return info.Error;

        var asset = LaunchPolicy.PickAsset(info.Assets, pattern);
        if (asset is null) return "Aucun installateur (.exe, .msi, .msix) trouvé dans la dernière release.";

        var version = info.LatestVersion is { } v ? $" ({v})" : "";
        if (!await platform.ConfirmAsync("Télécharger et installer",
                $"Télécharger puis exécuter :\n{asset.Name}{version}\n\nSource : {project.Source?.Url}"))
            return "Annulé.";

        var target = Path.Combine(downloadDirectory, project.Id, Path.GetFileName(asset.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        if (tokens.Get(TokenKeys.GitHub) is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var file = File.Create(target))
            await response.Content.CopyToAsync(file);

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return $"{asset.Name} téléchargé et lancé.";
    }
}
