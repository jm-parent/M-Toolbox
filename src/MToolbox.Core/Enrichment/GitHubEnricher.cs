using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MToolbox.Core.Models;

namespace MToolbox.Core.Enrichment;

/// <summary>Le HttpClient doit avoir BaseAddress = https://api.github.com/ : le PAT n'est jamais envoyé ailleurs.</summary>
public sealed partial class GitHubEnricher(HttpClient http, ITokenStore tokens) : IProjectEnricher
{
    public static readonly Uri ApiBase = new("https://api.github.com/");

    public bool CanHandle(Project project) =>
        project.Source is { Kind: SourceKind.GitHub } s && TryParse(s.Url, out _, out _);

    public static bool TryParse(string url, out string owner, out string repo)
    {
        owner = repo = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        owner = parts[0];
        repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return NamePattern().IsMatch(owner) && NamePattern().IsMatch(repo);
    }

    public async Task<ProjectEnrichment> EnrichAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project.Source is null || !TryParse(project.Source.Url, out var owner, out var repo))
            return new ProjectEnrichment { Error = "URL GitHub invalide." };

        var token = tokens.Get(TokenKeys.GitHub);
        var root = $"repos/{owner}/{repo}";

        try
        {
            using var repoResponse = await SendAsync(root, token, cancellationToken);
            if (!repoResponse.IsSuccessStatusCode)
                return new ProjectEnrichment { Error = DescribeFailure(repoResponse, token) };

            using var repoDoc = await ReadJsonAsync(repoResponse, cancellationToken);
            var lastActivity = ReadDate(repoDoc.RootElement, "pushed_at");

            var release = await GetReleaseAsync(root, token, cancellationToken);
            var contributors = await GetContributorsAsync(root, token, cancellationToken);
            var languages = await GetLanguagesAsync(root, token, cancellationToken);

            return release with
            {
                LastActivity = lastActivity,
                Contributors = contributors,
                Languages = languages,
                Description = repoDoc.RootElement.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or InvalidOperationException or KeyNotFoundException
                                       && !cancellationToken.IsCancellationRequested)
        {
            return new ProjectEnrichment { Error = $"Réponse GitHub inexploitable : {ex.Message}" };
        }
    }

    private async Task<ProjectEnrichment> GetReleaseAsync(string root, string? token, CancellationToken ct)
    {
        using var response = await SendAsync($"{root}/releases/latest", token, ct);
        if (!response.IsSuccessStatusCode) return new ProjectEnrichment();

        using var doc = await ReadJsonAsync(response, ct);
        var e = doc.RootElement;

        var assets = e.TryGetProperty("assets", out var arr)
            ? arr.EnumerateArray()
                 .Select(a => new ReleaseAsset(a.GetProperty("name").GetString()!, a.GetProperty("url").GetString()!))
                 .ToList()
            : [];

        return new ProjectEnrichment
        {
            LatestVersion = e.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null,
            LatestReleaseDate = ReadDate(e, "published_at"),
            Assets = assets,
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetDateTimeOffset() : null;

    private async Task<int?> GetContributorsAsync(string root, string? token, CancellationToken ct)
    {
        using var response = await SendAsync($"{root}/contributors?per_page=1&anon=1", token, ct);
        if (!response.IsSuccessStatusCode) return null;

        // Avec per_page=1, le numéro de la dernière page est le nombre de contributeurs.
        if (response.Headers.TryGetValues("Link", out var links) &&
            LastPagePattern().Match(string.Join(",", links)) is { Success: true } m)
            return int.Parse(m.Groups[1].Value);

        using var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.GetArrayLength();
    }

    private async Task<IReadOnlyList<string>> GetLanguagesAsync(string root, string? token, CancellationToken ct)
    {
        using var response = await SendAsync($"{root}/languages", token, ct);
        if (!response.IsSuccessStatusCode) return [];

        using var doc = await ReadJsonAsync(response, ct);
        return doc.RootElement.EnumerateObject()
                  .OrderByDescending(p => p.Value.GetInt64())
                  .Select(p => p.Name)
                  .Take(5)
                  .ToList();
    }

    private Task<HttpResponseMessage> SendAsync(string relativePath, string? token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http.SendAsync(request, ct);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

    private static string DescribeFailure(HttpResponseMessage response, string? token)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            return "Limite de requêtes GitHub atteinte : ajoutez un PAT dans les paramètres.";

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                string.IsNullOrEmpty(token)
                    ? "Dépôt introuvable ou privé : renseignez un PAT GitHub dans les paramètres."
                    : "Dépôt introuvable ou accès refusé avec le PAT GitHub configuré.",
            _ => $"GitHub a répondu {(int)response.StatusCode}.",
        };
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"[?&]page=(\d+)[^>]*>;\s*rel=""last""")]
    private static partial Regex LastPagePattern();
}
