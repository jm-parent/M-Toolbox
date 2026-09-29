using System.Globalization;
using MToolbox.Core.Models;

namespace MToolbox.Core.Catalogue;

public enum ProjectSort
{
    Activity,
    Name,
}

public sealed record ProjectQuery(
    string? Text = null,
    ProjectType? Type = null,
    IReadOnlySet<string>? Technologies = null,
    ProjectSort Sort = ProjectSort.Activity);

public static class ProjectFilter
{
    public static IReadOnlyList<Project> Apply(IEnumerable<Project> projects, ProjectQuery query)
    {
        var filtered = projects.Where(p =>
            (query.Type is null || p.Type == query.Type) &&
            (query.Technologies is not { Count: > 0 } ||
                p.Technologies.Any(t => query.Technologies.Contains(t, StringComparer.OrdinalIgnoreCase))) &&
            MatchesText(p, query.Text));

        return query.Sort switch
        {
            ProjectSort.Name => filtered.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => filtered.OrderByDescending(p => p.LastActivity ?? DateOnly.MinValue)
                         .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
    }

    public static IReadOnlyList<string> AllTechnologies(IEnumerable<Project> projects) =>
        projects.SelectMany(p => p.Technologies)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    private static bool MatchesText(Project p, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            Contains(p.Title, term) || Contains(p.Description, term) || Contains(p.Lead, term) ||
            p.Technologies.Any(t => Contains(t, term)));
    }

    private static bool Contains(string source, string term) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            source, term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
