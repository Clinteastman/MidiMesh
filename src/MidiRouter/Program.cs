using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MidiRouter;

/// <summary>
/// Custom entry point so a second launch shows the running window. Two copies would open the
/// same ports and send every note twice.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main()
    {
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
        // One copy per data folder. SHA-256, not string.GetHashCode, which changes every run.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDir.ToUpperInvariant()));
        var key = "MidiRouter-" + Convert.ToHexString(hash, 0, 8);
        var mainInstance = AppInstance.FindOrRegisterForKey(key);
        if (mainInstance.IsCurrent)
        {
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
