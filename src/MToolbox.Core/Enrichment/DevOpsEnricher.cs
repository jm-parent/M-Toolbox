using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MToolbox.Core.Models;

namespace MToolbox.Core.Enrichment;

/// <summary>Seul dev.azure.com est appelé : le PAT n'est jamais envoyé à un autre hôte.</summary>
public sealed class DevOpsEnricher(HttpClient http, ITokenStore tokens) : IProjectEnricher
{
    private const string ApiVersion = "7.1";

    public bool CanHandle(Project project) =>
        project.Source is { Kind: SourceKind.DevOps } s && TryParse(s.Url, out _, out _, out _);

    // https://dev.azure.com/{org}/{project}/_git/{repo}
    public static bool TryParse(string url, out string org, out string project, out string repo)
    {
        org = project = repo = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 4 || parts[2] != "_git") return false;

        (org, project, repo) = (parts[0], parts[1], parts[3]);
        return true;
    }

    public async Task<ProjectEnrichment> EnrichAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project.Source is null || !TryParse(project.Source.Url, out var org, out var proj, out var repo))
            return new ProjectEnrichment { Error = "URL Azure DevOps invalide." };

        var token = tokens.Get(TokenKeys.DevOps);
        if (string.IsNullOrEmpty(token))
            return new ProjectEnrichment { Error = "PAT Azure DevOps requis : renseignez-le dans les paramètres." };

        var root = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(proj)}/_apis";

        try
        {
            using var repoDoc = await GetJsonAsync($"{root}/git/repositories/{Uri.EscapeDataString(repo)}?api-version={ApiVersion}", token, cancellationToken);
            if (repoDoc is null)
                return new ProjectEnrichment { Error = "Accès refusé : vérifiez le PAT Azure DevOps (droit Code : lecture)." };

            var repoId = repoDoc.RootElement.GetProperty("id").GetString()!;

            using var commits = await GetJsonAsync(
                $"{root}/git/repositories/{repoId}/commits?searchCriteria.$top=200&api-version={ApiVersion}", token, cancellationToken);
            var (lastActivity, contributors) = ReadCommits(commits);

            using var builds = await GetJsonAsync(
                $"{root}/build/builds?repositoryId={repoId}&repositoryType=TfsGit&$top=1&api-version={ApiVersion}", token, cancellationToken);

            return new ProjectEnrichment
            {
                LastActivity = lastActivity,
                Contributors = contributors,
                BuildStatus = ReadBuildStatus(builds),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or InvalidOperationException or KeyNotFoundException
                                       && !cancellationToken.IsCancellationRequested)
        {
            return new ProjectEnrichment { Error = $"Azure DevOps injoignable ou réponse inexploitable : {ex.Message}" };
        }
    }

    // Retourne null si la réponse n'est pas du JSON (DevOps renvoie une page de connexion HTML sans authentification valide).
    private async Task<JsonDocument?> GetJsonAsync(string url, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}")));

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NonAuthoritativeInformation
            || response.Content.Headers.ContentType?.MediaType != "application/json")
            return null;

        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static (DateTimeOffset? LastActivity, int? Contributors) ReadCommits(JsonDocument? doc)
    {
        if (doc is null || !doc.RootElement.TryGetProperty("value", out var list) || list.GetArrayLength() == 0)
            return (null, null);

        var authors = list.EnumerateArray()
            .Select(c => c.TryGetProperty("author", out var a) && a.TryGetProperty("email", out var e) ? e.GetString() : null)
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var last = list[0].TryGetProperty("committer", out var c) && c.TryGetProperty("date", out var d) ? d.GetDateTimeOffset() : (DateTimeOffset?)null;
        return (last, authors);
    }

    private static string? ReadBuildStatus(JsonDocument? doc)
    {
        if (doc is null || !doc.RootElement.TryGetProperty("value", out var list) || list.GetArrayLength() == 0) return null;

        var build = list[0];
        var status = build.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status is "inProgress" or "notStarted") return "En cours";

        return (build.TryGetProperty("result", out var r) ? r.GetString() : null) switch
        {
            "succeeded" => "Réussi",
            "failed" => "Échec",
            "partiallySucceeded" => "Partiel",
            "canceled" => "Annulé",
            _ => null,
        };
    }
}
