using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using MToolbox.Core.Models;

namespace MToolbox.App.Services;

public interface ILogoService
{
    Task<Bitmap?> LoadAsync(Project project);
}

public sealed class LogoService(Uri catalogueSource, string cacheDirectory, HttpClient http) : ILogoService
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _gate = new(4);

    public async Task<Bitmap?> LoadAsync(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.Logo) || !Uri.TryCreate(catalogueSource, project.Logo, out var uri))
            return null;

        // Un catalogue distant ne doit pas pouvoir faire lire des fichiers locaux.
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsFile && catalogueSource.IsFile))
            return null;

        await _gate.WaitAsync();
        try
        {
            var bytes = uri.IsFile ? await File.ReadAllBytesAsync(uri.LocalPath) : await FetchCachedAsync(uri);
            if (bytes is null || bytes.Length > MaxBytes) return null;

            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // Logo absent ou illisible : la carte affiche simplement son titre.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]?> FetchCachedAsync(Uri uri)
    {
        var dir = Path.Combine(cacheDirectory, "logos");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..24]);

        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge)
            return await File.ReadAllBytesAsync(file);

        try
        {
            var bytes = await http.GetByteArrayAsync(uri);
            await File.WriteAllBytesAsync(file, bytes);
            return bytes;
        }
        catch (HttpRequestException)
        {
            return File.Exists(file) ? await File.ReadAllBytesAsync(file) : null;
        }
    }
}
