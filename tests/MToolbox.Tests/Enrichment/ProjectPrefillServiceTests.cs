using System.Net;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Enrichment;

public class ProjectPrefillServiceTests
{
    private static ProjectPrefillService Create(StubHandler github, string? devOpsToken = null) =>
        new([
            new GitHubEnricher(new HttpClient(github) { BaseAddress = GitHubEnricher.ApiBase }, new FakeTokenStore()),
            new DevOpsEnricher(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))), new FakeTokenStore(devops: devOpsToken)),
        ]);

    private static HttpResponseMessage GitHubApi(HttpRequestMessage req) => req.RequestUri!.AbsolutePath switch
    {
        "/repos/jm-parent/CreditsTracker" => StubHandler.Json("""{ "description": "Suivi de crédits", "pushed_at": "2026-09-25T23:30:00Z" }"""),
        "/repos/jm-parent/CreditsTracker/releases/latest" => StubHandler.Json("""
            { "tag_name": "v1.0.0", "published_at": "2026-09-25T10:00:00Z",
              "assets": [{ "name": "CreditsTracker-1.0.0-win32-x64-Setup.exe",
                           "url": "https://api.github.com/repos/jm-parent/CreditsTracker/releases/assets/1" }] }
            """),
        "/repos/jm-parent/CreditsTracker/contributors" => StubHandler.Json("""[{}, {}]"""),
        "/repos/jm-parent/CreditsTracker/languages" => StubHandler.Json("""{ "TypeScript": 50, "CSS": 5 }"""),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    [Fact]
    public async Task Prefill_GitHubUrl_FillsFormFromRepository()
    {
        var result = await Create(new StubHandler(GitHubApi)).PrefillAsync(" https://github.com/jm-parent/CreditsTracker.git ");

        Assert.Null(result.Message);
        var p = result.Project!;
        Assert.Equal("creditstracker", p.Id);
        Assert.Equal("CreditsTracker", p.Title);
        Assert.Equal("Suivi de crédits", p.Description);
        Assert.Equal(["TypeScript", "CSS"], p.Technologies);
        Assert.Equal(2, p.Contributors);
        Assert.Equal(new DateOnly(2026, 9, 25), p.LastActivity);
        Assert.Equal(new ProjectSource(SourceKind.GitHub, "https://github.com/jm-parent/CreditsTracker"), p.Source);
        Assert.Equal(new LaunchSpec(LaunchKind.GitHubRelease, "*Setup.exe"), p.Launch);
        Assert.Equal(ProjectType.WindowsApp, p.Type);
    }

    [Fact]
    public async Task Prefill_GitHubRepoNotFound_KeepsSourceAndWarns()
    {
        var result = await Create(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
            .PrefillAsync("https://github.com/jm-parent/CreditsTracker");

        Assert.NotNull(result.Project);
        Assert.Contains("partiel", result.Message);
    }

    [Fact]
    public async Task Prefill_DevOpsUrl_UsesRepositoryNameAndWarnsWithoutToken()
    {
        var result = await Create(new StubHandler(GitHubApi))
            .PrefillAsync("https://dev.azure.com/org/My%20Project/_git/PlanFactory");

        Assert.Equal("PlanFactory", result.Project!.Title);
        Assert.Equal(SourceKind.DevOps, result.Project.Source!.Kind);
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("not a url")]
    public async Task Prefill_UnknownUrl_ReturnsError(string url)
    {
        var result = await Create(new StubHandler(GitHubApi)).PrefillAsync(url);

        Assert.Null(result.Project);
        Assert.NotNull(result.Message);
    }
}
