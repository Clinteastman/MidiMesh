using System.Text.Json;

namespace MidiMesh.Appearance;

/// <summary>What sits behind the window's content.</summary>
public enum BackdropChoice
{
    /// <summary>Mica, darker variant (MicaBackdrop Kind="BaseAlt"). The default.</summary>
    MicaAlt,

    /// <summary>Mica, standard variant.</summary>
    Mica,

    /// <summary>See-through acrylic (DesktopAcrylicBackdrop).</summary>
    Acrylic,

    /// <summary>A plain solid colour, no effect.</summary>
    Solid,
}

/// <summary>The colour of tick boxes, the filter button, the activity lights and the icon.</summary>
public enum AccentChoice
{
    Green,
    Blue,
    Purple,
    Orange,
    Pink,
    Teal,

    /// <summary>Follow the accent colour set in Windows.</summary>
    Windows,
}

public enum ThemeChoice
{
    Dark,
    Light,

    /// <summary>Follow the app mode set in Windows (dark or light).</summary>
    Windows,
}

/// <summary>The appearance choices saved in settings.json.</summary>
public sealed record AppSettings
{
    public static AppSettings Default { get; } = new();

    public BackdropChoice Backdrop { get; init; } = BackdropChoice.MicaAlt;

    public AccentChoice Accent { get; init; } = AccentChoice.Green;

    public ThemeChoice Theme { get; init; } = ThemeChoice.Dark;
}

/// <summary>
/// Loads and saves settings.json in the data folder, next to routes.json. A value the app does not
/// know (a typo, or a choice from a newer version) falls back to its default on its own, so one bad
/// line never resets the other choices.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The file's shape. Strings rather than enums, so each value is checked on its own.</summary>
    private sealed class SettingsFile
    {
        public int Version { get; set; } = 1;

        public string? Backdrop { get; set; }

        public string? Accent { get; set; }

        public string? Theme { get; set; }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return AppSettings.Default;
            return Parse(File.ReadAllText(AppPaths.SettingsFile));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Info($"Could not read settings.json, using the default look: {ex.Message}");
            return AppSettings.Default;
        }
    }

    public static AppSettings Parse(string json)
    {
        var file = JsonSerializer.Deserialize<SettingsFile>(json, Options);
        if (file is null) return AppSettings.Default;
        var fallback = AppSettings.Default;
        return new AppSettings
        {
            Backdrop = Pick(file.Backdrop, fallback.Backdrop),
            Accent = Pick(file.Accent, fallback.Accent),
            Theme = Pick(file.Theme, fallback.Theme),
        };
    }

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(new SettingsFile
    {
        Backdrop = settings.Backdrop.ToString(),
        Accent = settings.Accent.ToString(),
        Theme = settings.Theme.ToString(),
    }, Options);

    public static void Save(AppSettings settings)
    {
        var temp = AppPaths.SettingsFile + ".tmp";
        try
        {
            File.WriteAllText(temp, Serialize(settings));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save settings.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Exact names only. Enum.TryParse would also take numbers ("7") and lists ("Green, Blue"),
    /// which can land on a choice nobody picked.
    /// </summary>
    private static T Pick<T>(string? text, T fallback) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text?.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null ? fallback : Enum.Parse<T>(name);
    }
}
