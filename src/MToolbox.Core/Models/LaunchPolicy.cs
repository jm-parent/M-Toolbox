using System.Text.RegularExpressions;
using MToolbox.Core.Enrichment;

namespace MToolbox.Core.Models;

/// <summary>Règles de sécurité appliquées avant d'ouvrir, télécharger ou lancer quoi que ce soit issu du catalogue.</summary>
public static class LaunchPolicy
{
    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1", ".lnk" };

    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".msi", ".msix", ".exe" };

    public static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    /// <summary>Chemin absolu (local ou UNC) vers un exécutable/script connu ou un dossier.</summary>
    public static bool IsAllowedPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return false;

        var extension = Path.GetExtension(value);
        return extension.Length == 0 || RunnableExtensions.Contains(extension);
    }

    /// <summary>Seuls les assets servis par l'API GitHub sont téléchargés (le PAT y est envoyé).</summary>
    public static bool IsAllowedAssetUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);

    public static ReleaseAsset? PickAsset(IEnumerable<ReleaseAsset> assets, string? pattern)
    {
        var candidates = assets.Where(a => IsAllowedAssetUrl(a.Url) && InstallerExtensions.Contains(Path.GetExtension(a.Name)));

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
            candidates = candidates.Where(a => regex.IsMatch(a.Name));
        }

        return candidates.FirstOrDefault();
    }
}
