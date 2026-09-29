using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Enrichment;

public class LaunchPolicyTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("calc.exe", false)]
    public void IsWebUrl_AllowsOnlyHttp(string value, bool expected) =>
        Assert.Equal(expected, LaunchPolicy.IsWebUrl(value));

    [Theory]
    [InlineData(@"\\srv\scripts\run.ps1", true)]
    [InlineData(@"C:\Tools\app.exe", true)]
    [InlineData(@"\\srv\scripts", true)]
    [InlineData(@"C:\Tools\document.docx", false)]
    [InlineData(@"..\run.exe", false)]
    [InlineData("run.exe", false)]
    [InlineData("", false)]
    public void IsAllowedPath_RequiresAbsoluteKnownTarget(string value, bool expected) =>
        Assert.Equal(expected, LaunchPolicy.IsAllowedPath(value));

    [Fact]
    public void PickAsset_MatchesPatternAndIgnoresNonInstallersAndForeignHosts()
    {
        ReleaseAsset[] assets =
        [
            new("RELEASES", "https://api.github.com/repos/a/b/releases/assets/1"),
            new("App-1.0-full.nupkg", "https://api.github.com/repos/a/b/releases/assets/2"),
            new("Evil-Setup.exe", "https://evil.example/Setup.exe"),
            new("App-1.0-win32-x64-Setup.exe", "https://api.github.com/repos/a/b/releases/assets/3"),
        ];

        Assert.Equal("App-1.0-win32-x64-Setup.exe", LaunchPolicy.PickAsset(assets, "*Setup.exe")?.Name);
        Assert.Equal("App-1.0-win32-x64-Setup.exe", LaunchPolicy.PickAsset(assets, null)?.Name);
        Assert.Null(LaunchPolicy.PickAsset(assets, "*.msi"));
    }
}

public class EnrichmentServiceTests
{
    private sealed class CountingEnricher(ProjectEnrichment result) : IProjectEnricher
    {
        public int Calls { get; private set; }
        public bool CanHandle(Project project) => true;

        public Task<ProjectEnrichment> EnrichAsync(Project project, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static readonly Project Sample = new() { Id = "p", Title = "P" };

    [Fact]
    public async Task Successful_results_are_cached_until_cleared()
    {
        var enricher = new CountingEnricher(new ProjectEnrichment { LatestVersion = "v1" });
        var service = new EnrichmentService([enricher]);

        await service.GetAsync(Sample);
        await service.GetAsync(Sample);
        service.Clear();
        await service.GetAsync(Sample);

        Assert.Equal(2, enricher.Calls);
    }

    [Fact]
    public async Task Errors_are_not_cached()
    {
        var enricher = new CountingEnricher(new ProjectEnrichment { Error = "boom" });
        var service = new EnrichmentService([enricher]);

        await service.GetAsync(Sample);
        await service.GetAsync(Sample);

        Assert.Equal(2, enricher.Calls);
    }
}
