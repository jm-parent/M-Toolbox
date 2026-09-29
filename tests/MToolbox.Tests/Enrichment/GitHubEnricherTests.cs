using System.Net;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Enrichment;

public class GitHubEnricherTests
{
    private static readonly Project Sample = new()
    {
        Id = "x", Title = "X",
        Source = new ProjectSource(SourceKind.GitHub, "https://github.com/jm-parent/CreditsTracker"),
    };

    private static GitHubEnricher Create(StubHandler handler, string? token = null) =>
        new(new HttpClient(handler) { BaseAddress = GitHubEnricher.ApiBase }, new FakeTokenStore(github: token));

    [Theory]
    [InlineData("https://github.com/jm-parent/CreditsTracker", "jm-parent", "CreditsTracker")]
    [InlineData("https://github.com/jm-parent/CreditsTracker.git", "jm-parent", "CreditsTracker")]
    [InlineData("https://github.com/jm-parent/CreditsTracker/releases", "jm-parent", "CreditsTracker")]
    public void TryParse_ExtractsOwnerAndRepo(string url, string owner, string repo)
    {
        Assert.True(GitHubEnricher.TryParse(url, out var o, out var r));
        Assert.Equal((owner, repo), (o, r));
    }

    [Theory]
    [InlineData("http://github.com/a/b")]
    [InlineData("https://evil.example/a/b")]
    [InlineData("https://github.com/a")]
    [InlineData("https://github.com/a/b%2F..")]
    public void TryParse_RejectsUnsafeOrIncompleteUrls(string url) =>
        Assert.False(GitHubEnricher.TryParse(url, out _, out _));

    [Fact]
    public async Task Enrich_ReadsReleaseContributorsLanguagesAndActivity()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/repos/jm-parent/CreditsTracker" => StubHandler.Json("""{ "pushed_at": "2026-09-25T10:00:00Z" }"""),
            "/repos/jm-parent/CreditsTracker/releases/latest" => StubHandler.Json("""
                { "tag_name": "v1.18.0", "published_at": "2026-09-25T10:00:00Z",
                  "assets": [{ "name": "CreditsTracker-1.18.0-win32-x64-Setup.exe",
                               "url": "https://api.github.com/repos/jm-parent/CreditsTracker/releases/assets/1" }] }
                """),
            "/repos/jm-parent/CreditsTracker/contributors" => WithLink(StubHandler.Json("""[{}]"""), "<https://api.github.com/x?per_page=1&anon=1&page=3>; rel=\"last\""),
            "/repos/jm-parent/CreditsTracker/languages" => StubHandler.Json("""{ "JavaScript": 10, "TypeScript": 50, "PowerShell": 5 }"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var result = await Create(handler).EnrichAsync(Sample);

        Assert.Null(result.Error);
        Assert.Equal("v1.18.0", result.LatestVersion);
        Assert.Equal(3, result.Contributors);
        Assert.Equal(["TypeScript", "JavaScript", "PowerShell"], result.Languages);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero), result.LastActivity);
        Assert.Single(result.Assets);
    }

    [Fact]
    public async Task Enrich_WithoutToken_ExplainsPrivateRepo_AndSendsNoAuthorization()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await Create(handler).EnrichAsync(Sample);

        Assert.Contains("PAT", result.Error);
        Assert.All(handler.Requests, r => Assert.Null(r.Headers.Authorization));
    }

    [Fact]
    public async Task Enrich_WithToken_SendsBearerOnlyToApiHost()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{}"));

        await Create(handler, token: "secret").EnrichAsync(Sample);

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("Bearer", r.Headers.Authorization?.Scheme);
            Assert.Equal("api.github.com", r.RequestUri!.Host);
        });
    }

    [Fact]
    public async Task Enrich_ReportsRateLimit()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");

        var result = await Create(new StubHandler(_ => response)).EnrichAsync(Sample);

        Assert.Contains("Limite", result.Error);
    }

    private static HttpResponseMessage WithLink(HttpResponseMessage response, string link)
    {
        response.Headers.Add("Link", link);
        return response;
    }
}
