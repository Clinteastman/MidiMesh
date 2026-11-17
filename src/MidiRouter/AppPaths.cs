namespace MidiRouter;

/// <summary>Where settings and the log live. MIDIROUTER_DATA_DIR overrides it for test runs.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    public static string RoutesFile => Path.Combine(DataDir, "routes.json");

    public static string LogFile => Path.Combine(DataDir, "log.txt");

    private static string ResolveDataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("MIDIROUTER_DATA_DIR");
        var dir = string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MidiRouter")
            : overrideDir;
        Directory.CreateDirectory(dir);
        return dir;
    }
}
