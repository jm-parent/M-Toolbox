using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.Core.Catalogue;

/// <summary>Règles de validation d'un nouveau projet et insertion dans catalogue.json (utilisées par l'app et par l'Action GitHub).</summary>
public static partial class ProjectSubmission
{
    private const int MaxTitle = 100, MaxDescription = 1000, MaxLead = 100, MaxTechnologies = 20, MaxTechnologyLength = 50, MaxLaunchValue = 500;

    private static readonly JsonSerializerOptions ValueOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string LogoPathFor(string id) => $"logos/{id}.png";

    public static string Slugify(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) ascii.Append(char.ToLowerInvariant(c));

        return SeparatorRun().Replace(ascii.ToString(), "-").Trim('-');
    }

    public static IReadOnlyList<string> Validate(Project project, IEnumerable<string> existingIds)
    {
        var errors = new List<string>();

        if (!IdPattern().IsMatch(project.Id ?? ""))
            errors.Add("L'identifiant doit être en minuscules, chiffres et tirets (ex. mon-projet).");
        else if (existingIds.Contains(project.Id!, StringComparer.OrdinalIgnoreCase))
            errors.Add($"Un projet avec l'identifiant « {project.Id} » existe déjà.");

        if (string.IsNullOrWhiteSpace(project.Title) || project.Title.Length > MaxTitle)
            errors.Add($"Le titre est obligatoire ({MaxTitle} caractères maximum).");
        if (project.Description.Length > MaxDescription)
            errors.Add($"La description dépasse {MaxDescription} caractères.");
        if (project.Lead.Length > MaxLead)
            errors.Add($"Le responsable dépasse {MaxLead} caractères.");
        if (!Enum.IsDefined(project.Type))
            errors.Add("Le type est invalide.");
        if (project.Contributors < 0)
            errors.Add("Le nombre de contributeurs doit être positif.");
        if (project.Technologies.Count > MaxTechnologies || project.Technologies.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTechnologyLength))
            errors.Add($"Technologies : {MaxTechnologies} maximum, {MaxTechnologyLength} caractères chacune.");
        if (project.Logo is not null && project.Logo != LogoPathFor(project.Id ?? ""))
            errors.Add($"Le logo doit être {LogoPathFor(project.Id ?? "")}.");

        ValidateSource(project.Source, errors);
        ValidateLaunch(project, errors);
        return errors;
    }

    /// <summary>Ajoute le projet à la fin du tableau en conservant la mise en forme existante du fichier.</summary>
    public static string AppendToCatalogue(string catalogueJson, Project project)
    {
        var catalogue = CatalogueSerializer.Deserialize(catalogueJson);
        var errors = Validate(project, catalogue.Projects.Select(p => p.Id));
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));

        var arrayEnd = catalogueJson.LastIndexOf(']');
        var lastObjectEnd = arrayEnd < 0 ? -1 : catalogueJson.LastIndexOf('}', arrayEnd);
        if (lastObjectEnd < 0) throw new InvalidDataException("Structure de catalogue inattendue.");

        var newline = catalogueJson.Contains("\r\n") ? "\r\n" : "\n";
        var result = catalogueJson[..(lastObjectEnd + 1)] + "," + newline + FormatEntry(project, newline) + catalogueJson[(lastObjectEnd + 1)..];

        // Garde-fou : le résultat doit rester un catalogue valide contenant le nouveau projet.
        var check = CatalogueSerializer.Deserialize(result);
        if (check.Projects.Count != catalogue.Projects.Count + 1)
            throw new InvalidDataException("L'insertion a produit un catalogue invalide.");
        return result;
    }

    private static void ValidateSource(ProjectSource? source, List<string> errors)
    {
        if (source is null) return;

        var valid = source.Kind switch
        {
            SourceKind.GitHub => GitHubEnricher.TryParse(source.Url, out _, out _),
            SourceKind.DevOps => DevOpsEnricher.TryParse(source.Url, out _, out _, out _),
            SourceKind.Script => LaunchPolicy.IsAllowedPath(source.Url),
            _ => false,
        };
        if (!valid) errors.Add("L'URL de la source est invalide pour le type de source choisi.");
    }

    private static void ValidateLaunch(Project project, List<string> errors)
    {
        if (project.Launch is not { } launch) return;

        var value = launch.Value ?? "";
        var valid = value.Length <= MaxLaunchValue && launch.Kind switch
        {
            LaunchKind.Url => LaunchPolicy.IsWebUrl(value),
            LaunchKind.Path => LaunchPolicy.IsAllowedPath(value),
            LaunchKind.Command => !string.IsNullOrWhiteSpace(value),
            LaunchKind.GitHubRelease => project.Source is { Kind: SourceKind.GitHub } && ReleasePattern().IsMatch(value),
            _ => false,
        };
        if (!valid) errors.Add("Le mode de lancement est invalide (GitHub release : source GitHub requise, motif du type *Setup.exe).");
    }

    private static string FormatEntry(Project p, string newline)
    {
        string S(string? value) => JsonSerializer.Serialize(value, ValueOptions);
        var fields = new List<string>
        {
            $"\"id\": {S(p.Id)}",
            $"\"title\": {S(p.Title)}",
        };
        if (p.Description.Length > 0) fields.Add($"\"description\": {S(p.Description)}");
        fields.Add($"\"type\": {JsonSerializer.Serialize(p.Type, CatalogueSerializer.Options)}");
        if (p.Logo is not null) fields.Add($"\"logo\": {S(p.Logo)}");
        if (p.Technologies.Count > 0) fields.Add($"\"technologies\": [{string.Join(", ", p.Technologies.Select(S))}]");
        if (p.Lead.Length > 0) fields.Add($"\"lead\": {S(p.Lead)}");
        fields.Add($"\"contributors\": {p.Contributors}");
        if (p.LastActivity is { } date) fields.Add($"\"lastActivity\": \"{date:yyyy-MM-dd}\"");
        if (p.Source is { } s)
            fields.Add($"\"source\": {{ \"kind\": {JsonSerializer.Serialize(s.Kind, CatalogueSerializer.Options)}, \"url\": {S(s.Url)} }}");
        if (p.Launch is { } l)
        {
            var value = l.Value is null ? "" : $", \"value\": {S(l.Value)}";
            fields.Add($"\"launch\": {{ \"kind\": {JsonSerializer.Serialize(l.Kind, CatalogueSerializer.Options)}{value} }}");
        }

        return "    {" + newline + string.Join("," + newline, fields.Select(f => "      " + f)) + newline + "    }";
    }

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SeparatorRun();

    [GeneratedRegex(@"^[\w.*?\- ]{1,100}$")]
    private static partial Regex ReleasePattern();
}
