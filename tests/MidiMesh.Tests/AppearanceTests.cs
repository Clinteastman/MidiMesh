using MidiMesh.Appearance;
using Xunit;

namespace MidiMesh.Tests;

public sealed class AppearanceTests
{
    [Fact]
    public void Settings_round_trip()
    {
        var settings = new AppSettings { Backdrop = BackdropChoice.Acrylic, Accent = AccentChoice.Windows, Theme = ThemeChoice.Light };
        Assert.Equal(settings, SettingsStore.Parse(SettingsStore.Serialize(settings)));
    }

    [Fact]
    public void Default_is_the_original_look()
    {
        var d = AppSettings.Default;
        Assert.Equal(BackdropChoice.MicaAlt, d.Backdrop);
        Assert.Equal(AccentChoice.Green, d.Accent);
        Assert.Equal(ThemeChoice.Dark, d.Theme);
    }

    [Fact]
    public void Unknown_values_fall_back_one_by_one()
    {
        var parsed = SettingsStore.Parse("""{ "backdrop": "Glass", "accent": "teal", "theme": "7" }""");
        Assert.Equal(BackdropChoice.MicaAlt, parsed.Backdrop);
        Assert.Equal(AccentChoice.Teal, parsed.Accent);
        Assert.Equal(ThemeChoice.Dark, parsed.Theme);
    }

    [Fact]
    public void Lists_and_numbers_are_not_taken_as_choices()
    {
        var parsed = SettingsStore.Parse("""{ "accent": "Green, Blue", "backdrop": "2" }""");
        Assert.Equal(AccentChoice.Green, parsed.Accent);
        Assert.Equal(BackdropChoice.MicaAlt, parsed.Backdrop);
    }

    [Fact]
    public void Missing_values_and_null_file_use_defaults()
    {
        Assert.Equal(AppSettings.Default, SettingsStore.Parse("{}"));
        Assert.Equal(AppSettings.Default, SettingsStore.Parse("null"));
        Assert.Equal(ThemeChoice.Light, SettingsStore.Parse("""{ "theme": "Light" }""").Theme);
    }

    [Fact]
    public void Green_keeps_the_original_shades()
    {
        var green = AccentPalette.ForPreset(AccentChoice.Green);
        Assert.Equal("#50DC6E", green.Base.ToString());
        Assert.Equal("#8DEDA0", green.Light2.ToString());
        Assert.Equal("#3FB859", green.Dark1.ToString());
        Assert.Equal("#50DC6E", green.LitColour(dark: true).ToString());
    }

    [Theory]
    [InlineData(AccentChoice.Blue)]
    [InlineData(AccentChoice.Purple)]
    [InlineData(AccentChoice.Orange)]
    [InlineData(AccentChoice.Pink)]
    [InlineData(AccentChoice.Teal)]
    public void Preset_shades_get_lighter_and_darker(AccentChoice choice)
    {
        var p = AccentPalette.ForPreset(choice);
        static int Sum(Rgb c) => c.R + c.G + c.B;
        Assert.True(Sum(p.Light3) > Sum(p.Light2) && Sum(p.Light2) > Sum(p.Light1) && Sum(p.Light1) > Sum(p.Base));
        Assert.True(Sum(p.Base) > Sum(p.Dark1) && Sum(p.Dark1) > Sum(p.Dark2) && Sum(p.Dark2) > Sum(p.Dark3));
        Assert.Equal(p.Base, p.LitColour(dark: true));
    }

    [Fact]
    public void Windows_accent_lights_use_its_light_shade()
    {
        var c = Rgb.FromHex;
        var p = AccentPalette.FromSystem(c("#0078D4"), c("#0093F9"), c("#60CCFE"), c("#98ECFE"), c("#005EB7"), c("#003D92"), c("#001968"));
        Assert.Equal("#0093F9", p.LitColour(dark: true).ToString());
        Assert.Equal("#005EB7", p.LitColour(dark: false).ToString());
    }

    [Fact]
    public void Tint_moves_green_to_the_new_colour_and_leaves_grey()
    {
        var green = AccentPalette.Green.Base;
        var blue = Rgb.FromHex("#4AA8FF");
        // BGRA: the icon's green, a dark grey, and green at half strength over that grey.
        byte[] pixels =
        [
            green.B, green.G, green.R, 255,
            0x30, 0x30, 0x30, 255,
            0x4F, 0x86, 0x40, 128,
        ];
        IconTint.TintBgra(pixels, green, blue);

        Assert.Equal(new byte[] { blue.B, blue.G, blue.R, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 0x30, 0x30, 0x30, 255 }, pixels[4..8]);
        Assert.Equal(128, pixels[11]); // Alpha untouched.
        Assert.True(pixels[8] > 0x4F && pixels[9] < 0x86); // More blue, less green.
    }
}
