using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Catalogue;

public class AddProjectIssueTests
{
    private const string Repo = "https://github.com/jm-parent/M-Toolbox";

    private static readonly Project Sample = new()
    {
        Id = "new-app", Title = "New App", Description = "Une appli & des \"guillemets\"",
        Type = ProjectType.WindowsApp, Technologies = ["C#"], Lead = "Jean",
        Source = new ProjectSource(SourceKind.GitHub, "https://github.com/jm-parent/App"),
    };

    private static string BodyWith(string json, string logo = "_No response_") =>
        $"### Projet (JSON)\n\n```json\n{json}\n```\n\n### Logo (optionnel)\n\n{logo}";

    [Fact]
    public void BuildRequest_PutsProjectJsonInTheUrl()
    {
        var request = AddProjectIssue.BuildRequest(Repo, Sample);

        Assert.True(request.JsonInUrl);
        Assert.StartsWith($"{Repo}/issues/new?template=add-project.yml", request.Url.AbsoluteUri);
        Assert.Contains("project-json=", request.Url.Query);
    }

    [Fact]
    public void BuildRequest_FallsBackToClipboardWhenTooLong()
    {
        var request = AddProjectIssue.BuildRequest(Repo, Sample with { Description = new string('x', 900), Technologies = Enumerable.Range(0, 20).Select(i => new string('t', 50)).ToList() });
        var huge = AddProjectIssue.BuildRequest(Repo, Sample with { Description = new string('é', 1000), Technologies = Enumerable.Range(0, 20).Select(i => new string('é', 50)).ToList() });

        Assert.True(request.JsonInUrl);
        Assert.False(huge.JsonInUrl);
        Assert.DoesNotContain("project-json", huge.Url.Query);
        Assert.Contains("\"id\":\"new-app\"", huge.ProjectJson);
    }

    [Fact]
    public void BuildRequest_JsonRoundTripsThroughIssueBody()
    {
        var json = AddProjectIssue.BuildRequest(Repo, Sample).ProjectJson;

        Assert.True(AddProjectIssue.TryReadProject(BodyWith(json), out var project, out var error), error);
        Assert.Equal(Sample.Technologies, project!.Technologies);
        Assert.Equal(Sample, project with { Technologies = Sample.Technologies });
    }

    [Fact]
    public void TryReadProject_HandlesWindowsLineEndings()
    {
        var json = AddProjectIssue.BuildRequest(Repo, Sample).ProjectJson;
        Assert.True(AddProjectIssue.TryReadProject(BodyWith(json).Replace("\n", "\r\n"), out _, out _));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"title\":\"x\"}")]
    [InlineData("{\"id\":\"x\",\"title\":\"x\",\"type\":\"nope\"}")]
    public void TryReadProject_RejectsInvalidJson(string json) =>
        Assert.False(AddProjectIssue.TryReadProject(BodyWith(json), out _, out var error) || error.Length == 0);

    [Fact]
    public void TryReadProject_RejectsBodyWithoutSection() =>
        Assert.False(AddProjectIssue.TryReadProject("hello", out _, out _));

    [Fact]
    public void TryReadProject_NormalizesExplicitNulls()
    {
        Assert.True(AddProjectIssue.TryReadProject(
            BodyWith("""{"id":"x","title":"X","description":null,"lead":null,"technologies":null}"""), out var project, out _));
        Assert.Equal(("", "", 0), (project!.Description, project.Lead, project.Technologies.Count));
    }

    [Theory]
    [InlineData("![logo](https://github.com/user-attachments/assets/1b2c-3d4e)", "https://github.com/user-attachments/assets/1b2c-3d4e")]
    [InlineData("<img width=\"64\" src=\"https://github.com/user-attachments/assets/aa-bb\" />", "https://github.com/user-attachments/assets/aa-bb")]
    public void FindLogoUrl_ReadsGitHubAttachments(string logoSection, string expected) =>
        Assert.Equal(expected, AddProjectIssue.FindLogoUrl(BodyWith("{}", logoSection))?.AbsoluteUri);

    [Theory]
    [InlineData("_No response_")]
    [InlineData("![x](https://evil.example/logo.png)")]
    [InlineData("![x](https://github.com.evil.example/user-attachments/assets/1)")]
    public void FindLogoUrl_IgnoresOtherHosts(string logoSection) =>
        Assert.Null(AddProjectIssue.FindLogoUrl(BodyWith("{}", logoSection)));
}
