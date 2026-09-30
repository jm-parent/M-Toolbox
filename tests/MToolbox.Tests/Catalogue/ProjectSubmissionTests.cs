using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Catalogue;

public class ProjectSubmissionTests
{
    private const string Existing = """
        {
          "schemaVersion": 1,
          "projects": [
            {
              "id": "one",
              "title": "One",
              "type": "tools",
              "technologies": ["C#"]
            }
          ]
        }
        """;

    private static Project Valid(string id = "new-app") => new() { Id = id, Title = "New App", Type = ProjectType.WebApp };

    [Theory]
    [InlineData("M'Toolbox", "m-toolbox")]
    [InlineData("Crédits  Tracker!", "credits-tracker")]
    [InlineData("  --Plan_Factory--  ", "plan-factory")]
    [InlineData("!!!", "")]
    public void Slugify_ProducesKebabCase(string title, string expected) =>
        Assert.Equal(expected, ProjectSubmission.Slugify(title));

    [Fact]
    public void Validate_AcceptsMinimalProject() =>
        Assert.Empty(ProjectSubmission.Validate(Valid(), ["one"]));

    [Theory]
    [InlineData("Bad Id")]
    [InlineData("UPPER")]
    [InlineData("")]
    public void Validate_RejectsMalformedId(string id) =>
        Assert.NotEmpty(ProjectSubmission.Validate(Valid(id), []));

    [Fact]
    public void Validate_RejectsDuplicateIdIgnoringCase() =>
        Assert.NotEmpty(ProjectSubmission.Validate(Valid("one"), ["ONE"]));

    [Fact]
    public void Validate_RejectsSourceUrlThatDoesNotMatchKind()
    {
        var project = Valid() with { Source = new ProjectSource(SourceKind.GitHub, "https://evil.example/a/b") };
        Assert.NotEmpty(ProjectSubmission.Validate(project, []));
    }

    [Theory]
    [InlineData(LaunchKind.Url, "javascript:alert(1)")]
    [InlineData(LaunchKind.Path, "relative\\tool.exe")]
    [InlineData(LaunchKind.Path, "C:\\tools\\payload.dll")]
    [InlineData(LaunchKind.GitHubRelease, "*Setup.exe")] // source GitHub manquante
    public void Validate_RejectsUnsafeLaunch(LaunchKind kind, string value)
    {
        var project = Valid() with { Launch = new LaunchSpec(kind, value) };
        Assert.NotEmpty(ProjectSubmission.Validate(project, []));
    }

    [Fact]
    public void Validate_AcceptsGitHubReleaseLaunchWithGitHubSource()
    {
        var project = Valid() with
        {
            Source = new ProjectSource(SourceKind.GitHub, "https://github.com/jm-parent/App"),
            Launch = new LaunchSpec(LaunchKind.GitHubRelease, "*Setup.exe"),
        };
        Assert.Empty(ProjectSubmission.Validate(project, []));
    }

    [Fact]
    public void Validate_OnlyAllowsTheLogoPathOfTheProject()
    {
        Assert.Empty(ProjectSubmission.Validate(Valid() with { Logo = "logos/new-app.png" }, []));
        Assert.NotEmpty(ProjectSubmission.Validate(Valid() with { Logo = "../../etc/passwd" }, []));
    }

    [Fact]
    public void AppendToCatalogue_KeepsExistingTextAndAddsProject()
    {
        var project = Valid() with
        {
            Description = "Une appli à « tester »",
            Technologies = ["React", "C#"],
            Lead = "Jean",
            Contributors = 2,
            LastActivity = new DateOnly(2026, 9, 30),
            Source = new ProjectSource(SourceKind.GitHub, "https://github.com/jm-parent/App"),
            Launch = new LaunchSpec(LaunchKind.GitHubRelease, "*Setup.exe"),
            Logo = "logos/new-app.png",
        };

        var result = ProjectSubmission.AppendToCatalogue(Existing, project);

        var prefixEnd = Existing.LastIndexOf('}', Existing.LastIndexOf(']')) + 1;
        Assert.StartsWith(Existing[..prefixEnd], result);
        var catalogue = CatalogueSerializer.Deserialize(result);
        Assert.Equal(["one", "new-app"], catalogue.Projects.Select(p => p.Id));
        Assert.Equal(project, catalogue.Projects[1] with { Technologies = project.Technologies });
        Assert.Contains("« tester »", result);
    }

    [Fact]
    public void AppendToCatalogue_PreservesCrLfLineEndings()
    {
        var crlf = Existing.Replace("\n", "\r\n");
        var result = ProjectSubmission.AppendToCatalogue(crlf, Valid());

        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Fact]
    public void AppendToCatalogue_RejectsDuplicateId() =>
        Assert.Throws<InvalidDataException>(() => ProjectSubmission.AppendToCatalogue(Existing, Valid("one")));

    [Fact]
    public void AppendToCatalogue_WorksOnTheRepositoryCatalogue()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "catalogue.json"))) dir = dir.Parent;
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "catalogue.json"));

        var result = ProjectSubmission.AppendToCatalogue(text, Valid("zz-test-project"));

        Assert.Equal(CatalogueSerializer.Deserialize(text).Projects.Count + 1, CatalogueSerializer.Deserialize(result).Projects.Count);
    }
}
