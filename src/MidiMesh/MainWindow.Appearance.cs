using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MidiMesh.Appearance;
using Windows.UI;

namespace MidiMesh;

/// <summary>The Appearance menu (backdrop, accent colour, theme) and applying those choices live.</summary>
public sealed partial class MainWindow
{
    private static readonly (BackdropChoice Choice, string Text)[] BackdropItems =
    [
        (BackdropChoice.MicaAlt, "Mica, darker"),
        (BackdropChoice.Mica, "Mica"),
        (BackdropChoice.Acrylic, "Acrylic (see-through)"),
        (BackdropChoice.Solid, "Solid"),
    ];

    private static readonly (AccentChoice Choice, string Text)[] AccentItems =
    [
        (AccentChoice.Green, "Green"),
        (AccentChoice.Blue, "Blue"),
        (AccentChoice.Purple, "Purple"),
        (AccentChoice.Orange, "Orange"),
        (AccentChoice.Pink, "Pink"),
        (AccentChoice.Teal, "Teal"),
        (AccentChoice.Windows, "Windows accent"),
    ];

    private static readonly (ThemeChoice Choice, string Text)[] ThemeItems =
    [
        (ThemeChoice.Dark, "Dark"),
        (ThemeChoice.Light, "Light"),
        (ThemeChoice.Windows, "Follow Windows"),
    ];

    private readonly List<(RadioMenuFlyoutItem Item, Func<AppSettings, bool> IsCurrent)> _appearanceItems = [];
    private FontIcon? _windowsAccentSwatch;
    // The window starts with the backdrop set in MainWindow.xaml.
    private BackdropChoice _backdrop = BackdropChoice.MicaAlt;

    /// <summary>One submenu, three groups, each a set of radio items. Built in code to keep the lists in one place.</summary>
    private void BuildAppearanceMenu()
    {
        AddGroup("BACKDROP", "Backdrop", BackdropItems, c => s => s.Backdrop == c, c => s => s with { Backdrop = c }, null);
        AppearanceItem.Items.Add(new MenuFlyoutSeparator());
        AddGroup("ACCENT COLOUR", "Accent", AccentItems, c => s => s.Accent == c, c => s => s with { Accent = c }, Swatch);
        AppearanceItem.Items.Add(new MenuFlyoutSeparator());
        AddGroup("THEME", "Theme", ThemeItems, c => s => s.Theme == c, c => s => s with { Theme = c }, null);
    }

    private void AddGroup<T>(string heading, string group, (T Choice, string Text)[] items,
        Func<T, Func<AppSettings, bool>> isCurrent, Func<T, Func<AppSettings, AppSettings>> change, Func<T, IconElement>? icon)
    {
        AppearanceItem.Items.Add(new MenuFlyoutItem { Text = heading, IsEnabled = false, FontSize = 11 });
        foreach (var (choice, text) in items)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = group };
            if (icon is not null) item.Icon = icon(choice);
            var apply = change(choice);
            item.Click += (_, _) => AppearanceManager.Change(apply);
            _appearanceItems.Add((item, isCurrent(choice)));
            AppearanceItem.Items.Add(item);
        }
    }

    /// <summary>A filled circle in the accent colour, beside its name.</summary>
    private IconElement Swatch(AccentChoice choice)
    {
        var swatch = new FontIcon
        {
            Glyph = "", // CircleFill
            Foreground = new SolidColorBrush(AppearanceManager.ToColor(AppearanceManager.SwatchColour(choice))),
        };
        if (choice == AccentChoice.Windows) _windowsAccentSwatch = swatch;
        return swatch;
    }

    /// <summary>Ticks the current choices; called each time the menu opens.</summary>
    private void SyncAppearanceMenu()
    {
        var settings = AppearanceManager.Settings;
        foreach (var (item, isCurrent) in _appearanceItems) item.IsChecked = isCurrent(settings);
        if (_windowsAccentSwatch?.Foreground is SolidColorBrush brush)
        {
            // The Windows accent can change while the app runs.
            brush.Color = AppearanceManager.ToColor(AppearanceManager.SwatchColour(AccentChoice.Windows));
        }
    }

    /// <summary>
    /// Applies the backdrop and theme. When the accent changed, the theme is flipped over and back
    /// in one go: WinUI only re-reads its accent colours when a theme is applied.
    /// </summary>
    public void ApplyAppearance(BackdropChoice backdrop, bool dark, bool accentChanged)
    {
        if (backdrop != _backdrop)
        {
            _backdrop = backdrop;
            SystemBackdrop = backdrop switch
            {
                BackdropChoice.MicaAlt => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                BackdropChoice.Mica => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                BackdropChoice.Acrylic => new DesktopAcrylicBackdrop(),
                _ => null,
            };
        }
        // Solid: the colour Windows uses for a plain window background.
        Root.Background = backdrop == BackdropChoice.Solid
            ? new SolidColorBrush(dark ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xF3, 0xF3, 0xF3))
            : null;

        if (accentChanged) Root.RequestedTheme = dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        ((SolidColorBrush)_stripeBrush).Color = dark ? Color.FromArgb(10, 255, 255, 255) : Color.FromArgb(12, 0, 0, 0);

        // Caption buttons: the system draws them, in the colours given here.
        var bar = AppWindow.TitleBar;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = Color.FromArgb(255, 140, 140, 140);
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(36, 0, 0, 0);

        UpdateLights();
    }

    /// <summary>The window, taskbar and title bar icon in the accent colour.</summary>
    public void SetIcons(string ico, string png)
    {
        AppWindow.SetIcon(ico);
        TitleIcon.Source = new BitmapImage(new Uri(png));
    }
}
