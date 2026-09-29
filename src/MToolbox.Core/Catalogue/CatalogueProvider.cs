using System.Net;
using System.Net.Http.Headers;
using MToolbox.Core.Models;

namespace MToolbox.Core.Catalogue;

public sealed record CatalogueResult(ProjectCatalogue Catalogue, DateTimeOffset SyncedAt, bool FromCache);

public interface ICatalogueProvider
{
    Task<CatalogueResult> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lit le catalogue depuis une URL ou un fichier local, avec cache disque et ETag.</summary>
public sealed class CatalogueProvider(Uri source, string cacheDirectory, HttpClient http) : ICatalogueProvider
{
    private string CacheFile => Path.Combine(cacheDirectory, "catalogue.json");
    private string ETagFile => Path.Combine(cacheDirectory, "catalogue.etag");

    public async Task<CatalogueResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);

        try
        {
            var json = source.IsFile
                ? await File.ReadAllTextAsync(source.LocalPath, cancellationToken)
                : await DownloadAsync(cancellationToken);

            if (json is not null)
            {
                var catalogue = CatalogueSerializer.Deserialize(json);
                if (!source.IsFile) await File.WriteAllTextAsync(CacheFile, json, cancellationToken);
                return new CatalogueResult(catalogue, DateTimeOffset.Now, FromCache: false);
            }

            File.SetLastWriteTime(CacheFile, DateTime.Now);
            return ReadCache() with { FromCache = false };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                       or System.Text.Json.JsonException or TaskCanceledException)
        {
            if (!File.Exists(CacheFile)) throw;
        }

        return ReadCache();
    }

    // null = 304, le cache est encore valide.
    private async Task<string?> DownloadAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (File.Exists(CacheFile) && File.Exists(ETagFile))
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(await File.ReadAllTextAsync(ETagFile, ct)));

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified) return null;
        response.EnsureSuccessStatusCode();

        if (response.Headers.ETag is { } etag)
            await File.WriteAllTextAsync(ETagFile, etag.ToString(), ct);

        return await response.Content.ReadAsStringAsync(ct);
    }

    private CatalogueResult ReadCache()
    {
        var catalogue = CatalogueSerializer.Deserialize(File.ReadAllText(CacheFile));
        return new CatalogueResult(catalogue, File.GetLastWriteTime(CacheFile), FromCache: true);
    }
}
