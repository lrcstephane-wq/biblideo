using Velopack;
using Velopack.Sources;

namespace Biblideo.App.Services;

public sealed class ApplicationUpdateService
{
    private const string RepositoryUrl = "https://github.com/lrcstephane-wq/biblideo";
    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _availableUpdate;

    public async Task<string?> CheckAsync()
    {
        try
        {
            _availableUpdate = await _manager.CheckForUpdatesAsync();
            return _availableUpdate?.TargetFullRelease.Version.ToString();
        }
        catch (NotInstalledException)
        {
            return null;
        }
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        if (_availableUpdate is null) return;
        await _manager.DownloadUpdatesAsync(_availableUpdate, progress);
        _manager.ApplyUpdatesAndRestart(_availableUpdate);
    }
}
