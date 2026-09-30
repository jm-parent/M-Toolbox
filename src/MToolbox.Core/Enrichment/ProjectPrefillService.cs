using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.Core.Enrichment;

/// <param name="Project">Brouillon prérempli, null si l'URL n'est pas reconnue.</param>
/// <param name="Message">Erreur (URL inconnue) ou avertissement (données partielles) à afficher.</param>
public sealed record PrefillResult(Project? Project, string? Message);

public interface IProjectPrefillService
{
    Task<PrefillResult> PrefillAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class ProjectPrefillService(IEnumerable<IProjectEnricher> enrichers) : IProjectPrefillService
{
    public async Task<PrefillResult> PrefillAsync(string url, CancellationToken cancellationToken = default)
    {
        url = url.Trim();
        string title;
        ProjectSource source;

        if (GitHubEnricher.TryParse(url, out var owner, out var repo))
            (title, source) = (repo, new ProjectSource(SourceKind.GitHub, $"https://github.com/{owner}/{repo}"));
        else if (DevOpsEnricher.TryParse(url, out var org, out var devOpsProject, out var devOpsRepo))
            (title, source) = (devOpsRepo, new ProjectSource(SourceKind.DevOps,
                $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(devOpsProject)}/_git/{Uri.EscapeDataString(devOpsRepo)}"));
        else
            return new PrefillResult(null, "URL non reconnue : attendu https://github.com/{org}/{dépôt} ou https://dev.azure.com/{org}/{projet}/_git/{dépôt}.");

        var draft = new Project { Id = ProjectSubmission.Slugify(title), Title = title, Source = source };
        var enricher = enrichers.FirstOrDefault(e => e.CanHandle(draft));
        var enrichment = enricher is null ? new ProjectEnrichment() : await enricher.EnrichAsync(draft, cancellationToken);

        var launch = source.Kind == SourceKind.GitHub ? GuessLaunch(enrichment) : null;
        var description = enrichment.Description ?? "";

        var filled = draft with
        {
            Description = description.Length > 1000 ? description[..1000] : description,
            Type = launch is null ? ProjectType.Tools : ProjectType.WindowsApp,
            Technologies = enrichment.Languages,
            Contributors = enrichment.Contributors ?? 0,
            LastActivity = enrichment.LastActivity is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : null,
            Launch = launch,
        };

        var warning = enrichment.Error is null ? null : $"Préremplissage partiel : {enrichment.Error}";
        return new PrefillResult(filled, warning);
    }

    // Motif d'après le suffixe du nom d'asset (ex. « App-1.2-Setup.exe » → « *Setup.exe »).
    private static LaunchSpec? GuessLaunch(ProjectEnrichment enrichment)
    {
        var asset = LaunchPolicy.PickAsset(enrichment.Assets, null);
        if (asset is null) return null;

        var cut = asset.Name.LastIndexOfAny(['-', '_']);
        var pattern = "*" + (cut >= 0 ? asset.Name[(cut + 1)..] : Path.GetExtension(asset.Name));
        return new LaunchSpec(LaunchKind.GitHubRelease, LaunchPolicy.PickAsset(enrichment.Assets, pattern) is null ? "*" + Path.GetExtension(asset.Name) : pattern);
    }
}
