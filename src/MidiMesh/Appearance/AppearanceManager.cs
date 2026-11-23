using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MidiMesh.Appearance;

/// <summary>
/// Holds the appearance choices, saves them, and applies them: the app theme and accent colours
/// at start, then live to the window, the tray icon and every control already on screen.
/// </summary>
public static class AppearanceManager
{
    // WinUI's accent keys, in the same order as AccentPalette.Shades.
    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    private static readonly UISettings WindowsUi = new();
    private static AccentPalette _applied = AccentPalette.Green;
    private static MainWindow? _window;
    private static Action<string>? _setTrayIcon;
    private static int _iconRequest;

    public static AppSettings Settings { get; private set; } = AppSettings.Default;

    /// <summary>Dark or light, after resolving "follow Windows".</summary>
    public static bool IsDark => Settings.Theme switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Windows => WindowsIsDark(),
        _ => true,
    };

    public static AccentPalette Palette => Settings.Accent == AccentChoice.Windows
        ? WindowsPalette()
        : AccentPalette.ForPreset(Settings.Accent);

    /// <summary>From the App constructor: the app theme can only be set before the app starts.</summary>
    public static void LoadAtStart(Application app)
    {
        Settings = SettingsStore.Load();
        app.RequestedTheme = IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    /// <summary>
    /// From OnLaunched, before the window is made (resources cannot be reached in the constructor).
    /// Accent colours set now reach every brush WinUI makes afterwards.
    /// </summary>
    public static void ApplyAccentColours() => ApplyAccent(Application.Current.Resources, Palette);

    /// <summary>Applies the saved look to the window and keeps it in step with Windows from now on.</summary>
    public static void Attach(MainWindow window, Action<string> setTrayIcon)
    {
        _window = window;
        _setTrayIcon = setTrayIcon;
        // Raised on a background thread when the Windows accent or app mode changes.
        WindowsUi.ColorValuesChanged += (_, _) => window.DispatcherQueue.TryEnqueue(() =>
        {
            if (Settings.Accent == AccentChoice.Windows || Settings.Theme == ThemeChoice.Windows) ApplyAll();
        });
        ApplyAll();
    }

    /// <summary>Changes one or more choices, saves them, and applies them straight away.</summary>
    public static void Change(Func<AppSettings, AppSettings> change)
    {
        var next = change(Settings);
        if (next == Settings) return;
        Settings = next;
        SettingsStore.Save(next);
        Log.Info($"Appearance: backdrop {next.Backdrop}, accent {next.Accent}, theme {next.Theme}.");
        ApplyAll();
    }

    /// <summary>The colour a preset or the Windows accent shows as, for the swatches in the menu.</summary>
    public static Rgb SwatchColour(AccentChoice choice) => choice == AccentChoice.Windows
        ? WindowsPalette().Bright
        : AccentPalette.PresetColours[choice];

    public static Color ToColor(Rgb c, byte alpha = 255) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static void ApplyAll()
    {
        var palette = Palette;
        var accentChanged = ApplyAccent(Application.Current.Resources, palette);
        _window?.ApplyAppearance(Settings.Backdrop, IsDark, accentChanged);
        _ = ApplyIconsAsync(palette.Bright);
    }

    private static async Task ApplyIconsAsync(Rgb colour)
    {
        var request = ++_iconRequest;
        var (ico, png) = await IconFiles.GetAsync(colour);
        if (request != _iconRequest) return; // A newer choice came in while this one was made.
        _window?.SetIcons(ico, png);
        _setTrayIcon?.Invoke(ico);
    }

    /// <summary>
    /// Sets WinUI's accent colours and the app's own theme-dependent brushes. Returns true when the
    /// accent changed: brushes WinUI already made only pick up new accent keys when the theme is
    /// applied again, which the window then does.
    /// </summary>
    /// <remarks>
    /// Recolouring WinUI's brushes in place by walking its resource dictionaries was tried; it
    /// stopped later theme changes from reaching the controls, so it is not done.
    /// </remarks>
    private static bool ApplyAccent(ResourceDictionary resources, AccentPalette palette)
    {
        var shades = palette.Shades;
        for (var i = 0; i < AccentKeys.Length; i++) resources[AccentKeys[i]] = ToColor(shades[i]);
        var changed = palette != _applied;
        _applied = palette;

        // The app's own brushes, set outright as they depend on the theme too.
        var dark = IsDark;
        SetBrush(resources, "LightOnBrush", ToColor(palette.LitColour(dark)));
        SetBrush(resources, "LightIdleBrush", ToColor(palette.IdleColour(dark)));
        SetBrush(resources, "WarningTextBrush", ToColor(Rgb.FromHex(dark ? "#F2B84B" : "#9D5D00")));
        return changed;
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color colour)
    {
        if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush) brush.Color = colour;
    }

    /// <summary>Windows reports a black background colour when apps are set to dark mode.</summary>
    private static bool WindowsIsDark()
    {
        var background = WindowsUi.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 3 * 128;
    }

    private static AccentPalette WindowsPalette()
    {
        Rgb Get(UIColorType type)
        {
            var c = WindowsUi.GetColorValue(type);
            return new Rgb(c.R, c.G, c.B);
        }
        return AccentPalette.FromSystem(
            Get(UIColorType.Accent),
            Get(UIColorType.AccentLight1), Get(UIColorType.AccentLight2), Get(UIColorType.AccentLight3),
            Get(UIColorType.AccentDark1), Get(UIColorType.AccentDark2), Get(UIColorType.AccentDark3));
    }
}
