using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Catalogue;

public class CatalogueSerializerTests
{
    [Fact]
    public void Deserialize_ParsesProjectsAndEnums()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "projects": [{
            "id": "a", "title": "A", "type": "windows-app",
            "technologies": ["C#"], "lastActivity": "2026-09-27",
            "source": { "kind": "devops", "url": "https://dev.azure.com/x" }
          }]
        }
        """;

        var project = Assert.Single(CatalogueSerializer.Deserialize(json).Projects);

        Assert.Equal(ProjectType.WindowsApp, project.Type);
        Assert.Equal(SourceKind.DevOps, project.Source!.Kind);
        Assert.Equal(new DateOnly(2026, 9, 27), project.LastActivity);
    }

    [Fact]
    public void Deserialize_RejectsDuplicateIds()
    {
        const string json = """
        { "schemaVersion": 1, "projects": [
          { "id": "a", "title": "A", "type": "tools" },
          { "id": "A", "title": "B", "type": "tools" } ] }
        """;

        Assert.Throws<InvalidDataException>(() => CatalogueSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_RejectsNewerSchema()
    {
        const string json = """{ "schemaVersion": 99, "projects": [] }""";

        Assert.Throws<InvalidDataException>(() => CatalogueSerializer.Deserialize(json));
    }

    [Fact]
    public void RepositoryCatalogue_IsValid()
    {
        var path = Path.Combine(FindRepoRoot(), "catalogue.json");

        var catalogue = CatalogueSerializer.Deserialize(File.ReadAllText(path));

        Assert.All(catalogue.Projects, p => Assert.False(string.IsNullOrWhiteSpace(p.Title)));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "catalogue.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("catalogue.json introuvable.");
    }
}
