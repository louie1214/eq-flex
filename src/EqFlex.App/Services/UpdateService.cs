using Velopack;
using Velopack.Sources;

namespace EqFlex.App.Services;

public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/louie1214/eq-flex";

    private UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            _pending = await _mgr.CheckForUpdatesAsync();
            return _pending;
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadAndInstallAsync(UpdateInfo update, Action<int>? onProgress = null)
    {
        if (_mgr is null) return;
        await _mgr.DownloadUpdatesAsync(update, onProgress);
        _mgr.ApplyUpdatesAndRestart(update);
    }
}
