using Microsoft.Win32;

namespace MidiMesh;

/// <summary>"Start with Windows": a value under the current user's Run key that starts the app in the tray.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The value the app used when it was called MIDI Router.</summary>
    public const string OldValueName = "MidiRouter";

    /// <summary>
    /// "MidiMesh" normally. A test run (MIDIMESH_DATA_DIR set) gets its own value, so it can never
    /// change the real one.
    /// </summary>
    public static string ValueName => AppPaths.IsOverridden ? AppInfo.Name + "-" + AppPaths.DataDirHash : AppInfo.Name;

    /// <summary>
    /// The installed app runs from %LOCALAPPDATA%\MidiMesh\current\MidiMesh.exe. Updates replace
    /// the files in "current" but keep that path, so this command keeps working after an update.
    /// </summary>
    public static string Command => CommandFor(Environment.ProcessPath ?? "");

    public static string CommandFor(string exePath) => $"\"{exePath}\" {TrayArgument}";

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

    /// <summary>
    /// If the old "MidiRouter" value is there, Start with Windows was on before the rename: remove
    /// it and add the "MidiMesh" value for this exe instead, so the user does not have to turn it
    /// on again. Returns true when it moved the value.
    /// </summary>
    public static bool MoveOldValue(string runKeyPath, string valueName, string command)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        if (key?.GetValue(OldValueName) is not string) return false;
        key.SetValue(valueName, command);
        key.DeleteValue(OldValueName, throwOnMissingValue: false);
        return true;
    }

    /// <summary>Run by the uninstaller, so Windows does not try to start a removed exe.</summary>
    public static void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
