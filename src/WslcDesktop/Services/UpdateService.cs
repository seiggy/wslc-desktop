using Velopack;
using Velopack.Sources;
using WslcDesktop.Updates;

namespace WslcDesktop.Services;

/// <summary>Checks GitHub Releases for a newer Velopack package, downloads it, and applies it on restart.</summary>
public sealed class UpdateService
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        try
        {
            var feed = Environment.GetEnvironmentVariable(UpdatePolicy.FeedOverrideVariable);
            var probe = new UpdateManager(new GithubSource(UpdatePolicy.RepositoryUrl, null, false));
            CurrentVersion = probe.CurrentVersion?.ToString();
            if (!probe.IsInstalled)
            {
                return;
            }

            _manager = string.IsNullOrWhiteSpace(feed)
                ? new UpdateManager(new GithubSource(UpdatePolicy.RepositoryUrl, null, UpdatePolicy.IncludePrereleases(CurrentVersion)))
                : new UpdateManager(feed);
        }
        catch (Exception)
        {
            // Not running from a Velopack install; updates stay unavailable.
        }
    }

    public string? CurrentVersion { get; }

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public string? AvailableVersion { get; private set; }

    public int Progress { get; private set; }

    public string? Error { get; private set; }

    public bool IsAvailable => _manager is not null;

    public event Action? Changed;

    public UpdateView View => UpdatePolicy.Describe(IsAvailable ? State : UpdateState.Unavailable, AvailableVersion, Progress, Error);

    public async Task CheckAsync()
    {
        if (_manager is null || State is UpdateState.Checking or UpdateState.Downloading)
        {
            return;
        }

        Set(UpdateState.Checking);
        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
            AvailableVersion = _pending?.TargetFullRelease.Version.ToString();
            Set(_pending is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Set(UpdateState.Error);
        }
    }

    public async Task DownloadAsync()
    {
        if (_manager is null || _pending is null || State != UpdateState.Available)
        {
            return;
        }

        Progress = 0;
        Set(UpdateState.Downloading);
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, p =>
            {
                Progress = UpdatePolicy.ClampProgress(p);
                Changed?.Invoke();
            });
            Set(UpdateState.ReadyToInstall);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Set(UpdateState.Error);
        }
    }

    /// <summary>Exits the app, applies the downloaded update, and relaunches.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not null && _pending is not null && State == UpdateState.ReadyToInstall)
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
