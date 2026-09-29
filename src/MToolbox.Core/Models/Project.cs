using System.Text.Json.Serialization;

namespace MToolbox.Core.Models;

public enum ProjectType
{
    [JsonStringEnumMemberName("windows-app")] WindowsApp,
    [JsonStringEnumMemberName("web-app")] WebApp,
    [JsonStringEnumMemberName("tools")] Tools,
    [JsonStringEnumMemberName("script")] Script,
}

public enum SourceKind
{
    [JsonStringEnumMemberName("github")] GitHub,
    [JsonStringEnumMemberName("devops")] DevOps,
    [JsonStringEnumMemberName("script")] Script,
}

public sealed record ProjectSource(SourceKind Kind, string Url);

public enum LaunchKind
{
    [JsonStringEnumMemberName("url")] Url,
    [JsonStringEnumMemberName("path")] Path,
    [JsonStringEnumMemberName("command")] Command,
    [JsonStringEnumMemberName("github-release")] GitHubRelease,
}

/// <summary>Value : URL, chemin, commande, ou motif du fichier de release (ex. *.msi) selon Kind.</summary>
public sealed record LaunchSpec(LaunchKind Kind, string? Value = null);

public sealed record Project
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public ProjectType Type { get; init; }
    public string? Logo { get; init; }
    public IReadOnlyList<string> Technologies { get; init; } = [];
    public string Lead { get; init; } = "";
    public int Contributors { get; init; }
    public DateOnly? LastActivity { get; init; }
    public ProjectSource? Source { get; init; }
    public LaunchSpec? Launch { get; init; }
}

public sealed record ProjectCatalogue
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<Project> Projects { get; init; } = [];
}
