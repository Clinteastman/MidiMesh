using System.Reflection;

namespace MidiMesh;

/// <summary>
/// The product name, version and home page in one place. The version itself is set once, in
/// MidiMesh.csproj (or by the release build from the git tag), and read back from the assembly here.
/// </summary>
public static class AppInfo
{
    public const string Name = "MidiMesh";

    /// <summary>Release feed and home page. Set by the RepositoryUrl property in the csproj.</summary>
    public static string RepositoryUrl { get; } = Metadata("RepositoryUrl") ?? "";

    /// <summary>For example "0.1.0". Build metadata after "+" (the commit hash) is left off.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : info[..plus];
    }

    private static string? Metadata(string key) =>
        typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;
}
