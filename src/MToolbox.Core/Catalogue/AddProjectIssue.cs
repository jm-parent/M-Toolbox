using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MToolbox.Core.Models;

namespace MToolbox.Core.Catalogue;

public sealed record IssueRequest(Uri Url, string ProjectJson, bool JsonInUrl);

/// <summary>Format partagé entre l'app (création de l'issue) et l'Action GitHub (lecture de l'issue) ; les titres doivent rester identiques à ceux de .github/ISSUE_TEMPLATE/add-project.yml.</summary>
public static partial class AddProjectIssue
{
    public const string ProjectHeading = "Projet (JSON)";
    public const string LogoHeading = "Logo (optionnel)";
    public const string Label = "add-project";

    // Au-delà, GitHub refuse l'URL : le JSON est alors copié dans le presse-papiers.
    private const int MaxUrlLength = 7000;

    private static readonly JsonSerializerOptions CompactOptions = new(CatalogueSerializer.Options)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IssueRequest BuildRequest(string repoUrl, Project project)
    {
        // Le logo est ajouté par l'Action à partir de la pièce jointe de l'issue.
        var json = JsonSerializer.Serialize(project with { Logo = null }, CompactOptions);
        var title = $"Ajouter le projet : {project.Title}";
        var baseUrl = $"{repoUrl.TrimEnd('/')}/issues/new?template=add-project.yml&title={Uri.EscapeDataString(title)}";

        var withJson = $"{baseUrl}&project-json={Uri.EscapeDataString(json)}";
        var inUrl = withJson.Length <= MaxUrlLength;
        return new IssueRequest(new Uri(inUrl ? withJson : baseUrl), json, inUrl);
    }

    public static bool TryReadProject(string issueBody, out Project? project, out string error)
    {
        project = null;
        var section = Section(issueBody, ProjectHeading);
        if (section is null) { error = "Section « Projet (JSON) » introuvable."; return false; }

        try
        {
            var parsed = JsonSerializer.Deserialize<Project>(StripFence(section), CatalogueSerializer.Options);
            if (parsed is null) { error = "JSON vide."; return false; }

            // Un null explicite dans le JSON contourne les valeurs par défaut du modèle.
            project = parsed with { Description = parsed.Description ?? "", Lead = parsed.Lead ?? "", Technologies = parsed.Technologies ?? [] };
            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON invalide : {ex.Message}";
            return false;
        }
    }

    /// <summary>Retourne l'URL de la première pièce jointe GitHub de la section logo, ou null.</summary>
    public static Uri? FindLogoUrl(string issueBody)
    {
        var section = Section(issueBody, LogoHeading);
        var match = section is null ? null : AttachmentUrl().Match(section);
        return match is { Success: true } && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static string? Section(string body, string heading)
    {
        var text = "\n" + body.Replace("\r\n", "\n");
        var marker = $"\n### {heading}\n";
        var found = text.IndexOf(marker, StringComparison.Ordinal);
        if (found < 0) return null;

        var start = found + marker.Length;
        var end = text.IndexOf("\n### ", start, StringComparison.Ordinal);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    private static string StripFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal)) return value;

        var firstNewline = value.IndexOf('\n');
        var body = firstNewline < 0 ? "" : value[(firstNewline + 1)..];
        return body.EndsWith("```", StringComparison.Ordinal) ? body[..^3].Trim() : body.Trim();
    }

    [GeneratedRegex(@"https://(?:github\.com/user-attachments/(?:assets|files)/|user-images\.githubusercontent\.com/)[A-Za-z0-9/_.%-]+")]
    private static partial Regex AttachmentUrl();
}
