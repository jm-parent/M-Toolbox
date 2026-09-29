using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.Tests.Catalogue;

public class ProjectFilterTests
{
    private static readonly Project[] Projects =
    [
        new() { Id = "krypton", Title = "Krypton", Type = ProjectType.WindowsApp, Lead = "Sophie L.",
                Technologies = ["Avalonia", "C#"], LastActivity = new DateOnly(2026, 9, 1) },
        new() { Id = "pulse", Title = "Pulse", Type = ProjectType.WebApp, Lead = "Marc K.",
                Technologies = ["Go"], Description = "Télémétrie temps réel", LastActivity = new DateOnly(2026, 9, 20) },
        new() { Id = "atlas", Title = "Atlas", Type = ProjectType.Script, Lead = "Emma B.",
                Technologies = ["Python"] },
    ];

    [Fact]
    public void Search_MatchesLeadTechnologyAndIgnoresAccents()
    {
        Assert.Equal(["krypton"], Ids(new ProjectQuery(Text: "sophie")));
        Assert.Equal(["krypton"], Ids(new ProjectQuery(Text: "AVALONIA")));
        Assert.Equal(["pulse"], Ids(new ProjectQuery(Text: "telemetrie")));
    }

    [Fact]
    public void Search_RequiresAllTerms()
    {
        Assert.Empty(Ids(new ProjectQuery(Text: "krypton go")));
    }

    [Fact]
    public void FilterByTypeAndTechnologies()
    {
        Assert.Equal(["pulse"], Ids(new ProjectQuery(Type: ProjectType.WebApp)));
        Assert.Equal(["atlas"], Ids(new ProjectQuery(Technologies: new HashSet<string> { "python" })));
    }

    [Fact]
    public void Sort_ByActivityPutsUndatedLast_ByNameIsAlphabetical()
    {
        Assert.Equal(["pulse", "krypton", "atlas"], Ids(new ProjectQuery()));
        Assert.Equal(["atlas", "krypton", "pulse"], Ids(new ProjectQuery(Sort: ProjectSort.Name)));
    }

    private static string[] Ids(ProjectQuery query) =>
        ProjectFilter.Apply(Projects, query).Select(p => p.Id).ToArray();
}
