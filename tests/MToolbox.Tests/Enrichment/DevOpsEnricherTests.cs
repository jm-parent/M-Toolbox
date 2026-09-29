using System.Net;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Enrichment;

public class DevOpsEnricherTests
{
    private static readonly Project Sample = new()
    {
        Id = "d", Title = "D",
        Source = new ProjectSource(SourceKind.DevOps, "https://dev.azure.com/acme/My%20Project/_git/my-repo"),
    };

    [Fact]
    public void TryParse_ExtractsOrgProjectRepo()
    {
        Assert.True(DevOpsEnricher.TryParse(Sample.Source!.Url, out var org, out var project, out var repo));
        Assert.Equal(("acme", "My Project", "my-repo"), (org, project, repo));
    }

    [Theory]
    [InlineData("https://acme.visualstudio.com/p/_git/r")]
    [InlineData("https://dev.azure.com/acme/p/r")]
    [InlineData("http://dev.azure.com/acme/p/_git/r")]
    public void TryParse_RejectsOtherShapes(string url) =>
        Assert.False(DevOpsEnricher.TryParse(url, out _, out _, out _));

    [Fact]
    public async Task Enrich_WithoutToken_DoesNotCallNetwork()
    {
        var handler = new StubHandler(_ => StubHandler.Json("{}"));

        var result = await new DevOpsEnricher(new HttpClient(handler), new FakeTokenStore()).EnrichAsync(Sample);

        Assert.Contains("PAT", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Enrich_HtmlSignInPage_ReportsAccessDenied()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("<html>sign in</html>", System.Text.Encoding.UTF8, "text/html") });

        var result = await new DevOpsEnricher(new HttpClient(handler), new FakeTokenStore(devops: "pat")).EnrichAsync(Sample);

        Assert.Contains("Accès refusé", result.Error);
    }

    [Fact]
    public async Task Enrich_ReadsActivityContributorsAndBuild()
    {
        var handler = new StubHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (url.Contains("/commits")) return StubHandler.Json("""
                { "value": [
                  { "author": { "email": "a@x" }, "committer": { "date": "2026-09-20T08:00:00Z" } },
                  { "author": { "email": "B@x" } }, { "author": { "email": "b@x" } } ] }
                """);
            if (url.Contains("/build/builds")) return StubHandler.Json("""{ "value": [{ "status": "completed", "result": "succeeded" }] }""");
            return StubHandler.Json("""{ "id": "repo-id" }""");
        });

        var result = await new DevOpsEnricher(new HttpClient(handler), new FakeTokenStore(devops: "pat")).EnrichAsync(Sample);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Contributors);
        Assert.Equal("Réussi", result.BuildStatus);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero), result.LastActivity);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("dev.azure.com", r.RequestUri!.Host);
            Assert.Equal("Basic", r.Headers.Authorization?.Scheme);
        });
    }
}
