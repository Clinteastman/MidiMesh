using Microsoft.Win32;

namespace MidiRouter;

/// <summary>"Start with Windows": a value under the current user's Run key that starts the app in the tray.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MidiRouter";

    private static string Command => $"\"{Environment.ProcessPath}\" {TrayArgument}";

    /// <summary>True when Windows will start this exe at sign-in.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Info($"Start with Windows {(enabled ? "on" : "off")}.");
    }
}
