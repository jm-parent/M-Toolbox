using MToolbox.Core.Models;

namespace MToolbox.Core.Enrichment;

public sealed record ReleaseAsset(string Name, string Url);

public sealed record ProjectEnrichment
{
    public string? LatestVersion { get; init; }
    public DateTimeOffset? LatestReleaseDate { get; init; }
    public DateTimeOffset? LastActivity { get; init; }
    public int? Contributors { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
    public string? BuildStatus { get; init; }
    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];
    public string? Error { get; init; }
}

public interface IProjectEnricher
{
    bool CanHandle(Project project);

    /// <summary>Ne lève pas d'exception réseau : les échecs sont retournés dans <see cref="ProjectEnrichment.Error"/>.</summary>
    Task<ProjectEnrichment> EnrichAsync(Project project, CancellationToken cancellationToken = default);
}

public interface ITokenStore
{
    string? Get(string key);
    void Set(string key, string? value);
}

public static class TokenKeys
{
    public const string GitHub = "github";
    public const string DevOps = "devops";
}
