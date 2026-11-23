using Velopack;
using Velopack.Sources;

namespace MidiMesh;

/// <summary>
/// Updates through Velopack. The feed is the GitHub Releases page of the repository (AppInfo),
/// or MIDIMESH_UPDATE_URL (a folder or web address) to test against a local feed. Updates
/// download in the background; the restart that applies one is always the user's choice,
/// because restarting stops the routing for a moment.
/// </summary>
public static class Updater
{
    public const string FeedVariable = "MIDIMESH_UPDATE_URL";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static UpdateManager? _manager;
    private static UpdateInfo? _ready;

    /// <summary>Raised once an update has downloaded and only needs a restart.</summary>
    public static event Action<string>? UpdateReady;

    /// <summary>False when running from a build folder rather than an install or the portable zip.</summary>
    public static bool IsInstalled => Manager?.IsInstalled == true;

    /// <summary>The version waiting for a restart, or null.</summary>
    public static string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    private static UpdateManager? Manager
    {
        get
        {
            if (_manager is not null) return _manager;
            try
            {
                var feed = Environment.GetEnvironmentVariable(FeedVariable);
                _manager = string.IsNullOrWhiteSpace(feed)
                    ? new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false))
                    : new UpdateManager(feed);
            }
            catch (Exception ex)
            {
                Log.Info($"Updates unavailable: {ex.Message}");
            }
            return _manager;
        }
    }

    /// <summary>
    /// Checks the feed and downloads a newer version if there is one. Returns a short sentence for
    /// the user. Never throws: a failed check must not upset the routing.
    /// </summary>
    public static async Task<string> CheckAndDownloadAsync()
    {
        if (!IsInstalled) return "Updates work in the installed app. This copy runs from a build folder.";
        if (!await Gate.WaitAsync(0)) return "Already checking for updates.";
        try
        {
            if (_ready is not null) return $"Version {ReadyVersion} is ready. Restart {AppInfo.Name} to finish.";
            var update = await Manager!.CheckForUpdatesAsync();
            if (update is null) return $"You have the latest version ({AppInfo.Version}).";

            var version = update.TargetFullRelease.Version.ToString();
            Log.Info($"Update: downloading {version}.");
            await Manager.DownloadUpdatesAsync(update);
            _ready = update;
            Log.Info($"Update: {version} ready.");
            UpdateReady?.Invoke(version);
            return $"Version {version} is ready. Restart {AppInfo.Name} to finish.";
        }
        catch (Exception ex)
        {
            Log.Info($"Update check failed: {ex.Message}");
            return "Could not check for updates. Check the internet connection and try again.";
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Hands the downloaded update to Velopack's updater, which waits for this process to exit,
    /// swaps the files and starts the new version. The caller then quits normally, so the tray
    /// icon and MIDI ports are closed properly first.
    /// </summary>
    public static bool PrepareRestart()
    {
        if (_ready is null || Manager is null) return false;
        try
        {
            Manager.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: true);
            Log.Info($"Update: restarting into {ReadyVersion}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Info($"Update could not start: {ex.Message}");
            return false;
        }
    }
}
