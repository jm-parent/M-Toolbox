using System.Collections.Concurrent;
using MToolbox.Core.Models;

namespace MToolbox.Core.Enrichment;

public interface IEnrichmentService
{
    Task<ProjectEnrichment?> GetAsync(Project project, CancellationToken cancellationToken = default);
    void Clear();
}

/// <summary>Cache mémoire : évite de dépasser les limites d'API en rouvrant plusieurs fois la même fiche.</summary>
public sealed class EnrichmentService(IEnumerable<IProjectEnricher> enrichers, TimeSpan? ttl = null) : IEnrichmentService
{
    private readonly IReadOnlyList<IProjectEnricher> _enrichers = enrichers.ToList();
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, ProjectEnrichment Value)> _cache = new();

    public async Task<ProjectEnrichment?> GetAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(project.Id, out var hit) && DateTimeOffset.Now - hit.At < _ttl)
            return hit.Value;

        var enricher = _enrichers.FirstOrDefault(e => e.CanHandle(project));
        if (enricher is null) return null;

        var result = await enricher.EnrichAsync(project, cancellationToken);
        if (result.Error is null) _cache[project.Id] = (DateTimeOffset.Now, result);
        return result;
    }

    public void Clear() => _cache.Clear();
}
