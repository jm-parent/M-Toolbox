using Velopack;
using Velopack.Sources;

namespace MToolbox.App.Services;

public interface IUpdateService
{
    /// <summary>Retourne la version disponible, ou null si à jour / non installé via Velopack.</summary>
    Task<string?> CheckAsync();

    Task ApplyAndRestartAsync();
}

public sealed class UpdateService(string repoUrl) : IUpdateService
{
    private readonly UpdateManager _manager = new(new GithubSource(repoUrl, null, false));
    private UpdateInfo? _pending;

    public async Task<string?> CheckAsync()
    {
        if (!_manager.IsInstalled) return null;

        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task ApplyAndRestartAsync()
    {
        if (_pending is null) return;

        await _manager.DownloadUpdatesAsync(_pending);
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
