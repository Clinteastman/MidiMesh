using System.Security.Cryptography;
using System.Text;

namespace MidiMesh;

/// <summary>
/// Where settings and the log live: %APPDATA%\MidiMesh. Not %LOCALAPPDATA%\MidiMesh, because the
/// installer puts the app itself there and removes that whole folder on uninstall.
/// MIDIMESH_DATA_DIR overrides it for test runs, which also gives the test copy its own
/// single-instance key and its own Start with Windows value.
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "MIDIMESH_DATA_DIR";

    /// <summary>True when MIDIMESH_DATA_DIR is set: a test run that must leave the real settings alone.</summary>
    public static bool IsOverridden { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirVariable));

    public static string DataDir { get; } = ResolveDataDir();

    public static string RoutesFile => Path.Combine(DataDir, "routes.json");

    public static string LogFile => Path.Combine(DataDir, "log.txt");

    /// <summary>Appearance choices: backdrop, accent colour, theme.</summary>
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Copies of the icon recoloured to the chosen accent.</summary>
    public static string IconCacheDir => Path.Combine(DataDir, "icons");

    /// <summary>Short id of the data folder, for the single-instance key and test-run names.</summary>
    public static string DataDirHash { get; } =
        // SHA-256, not string.GetHashCode, which changes every run.
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDir.ToUpperInvariant())), 0, 8);

    /// <summary>Where the app kept its files when it was called MIDI Router.</summary>
    public static string OldDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MidiRouter");

    private static string ResolveDataDir()
    {
        var dir = IsOverridden
            ? Environment.GetEnvironmentVariable(DataDirVariable)!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
