using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using Velopack;

namespace MidiMesh;

/// <summary>
/// Custom entry point so a second launch shows the running window. Two copies would open the
/// same ports and send every note twice.
/// </summary>
public static class Program
{
    /// <summary>Held by the running copy, so a new launch can tell one is running before anything else starts.</summary>
    private static Mutex? _runningMutex;

    [STAThread]
    private static int Main()
    {
        // First, before anything else: the installer, updater and uninstaller start the exe with
        // special arguments, and Velopack handles those here and exits. On a normal start it also
        // applies an update that downloaded earlier but was not restarted into. Only do that when
        // no copy is running: otherwise opening the app from the Start menu would stop the running
        // copy mid-performance to update it.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!IsVelopackHook() && !IsAnotherCopyRunning())
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Remove())
            .Run();

        MoveFromOldName();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance()) return 0;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        // One copy per data folder.
        var key = AppInfo.Name + "-" + AppPaths.DataDirHash;
        var mainInstance = AppInstance.FindOrRegisterForKey(key);
        if (mainInstance.IsCurrent)
        {
            _runningMutex = new Mutex(initiallyOwned: true, RunningMutexName);
            mainInstance.Activated += (_, _) => App.ShowRunningWindow();
            return false;
        }

        AllowSetForegroundWindow(mainInstance.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var redirected = CreateEvent(IntPtr.Zero, true, false, null);
        _ = Task.Run(() =>
        {
            try { mainInstance.RedirectActivationToAsync(activation).AsTask().Wait(); }
            finally { SetEvent(redirected); }
        });
        // Pump COM while waiting so the cross-process call can complete on this STA thread.
        _ = CoWaitForMultipleObjects(0, uint.MaxValue, 1, [redirected], out _);
        CloseHandle(redirected);
        return true;
    }

    private static string RunningMutexName => $@"Local\{AppInfo.Name}-running-{AppPaths.DataDirHash}";

    private static bool IsVelopackHook() =>
        Environment.GetCommandLineArgs().Any(a => a.StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase));

    private static bool IsAnotherCopyRunning()
    {
        if (!Mutex.TryOpenExisting(RunningMutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>
    /// The app used to be called MIDI Router. Bring its routes, log and Start with Windows setting
    /// across once. Skipped for test runs (MIDIMESH_DATA_DIR set) so they never touch real settings.
    /// </summary>
    private static void MoveFromOldName()
    {
        if (AppPaths.IsOverridden) return;
        try
        {
            if (Migration.CopyOldData(AppPaths.OldDataDir, AppPaths.DataDir))
            {
                Log.Info($"Copied routes and log from {AppPaths.OldDataDir}.");
            }
            if (StartupRegistration.MoveOldValue(StartupRegistration.RunKey, StartupRegistration.ValueName, StartupRegistration.Command))
            {
                Log.Info("Moved Start with Windows from MidiRouter to MidiMesh.");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"Could not move settings from MIDI Router: {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
