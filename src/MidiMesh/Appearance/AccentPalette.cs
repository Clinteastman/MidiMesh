namespace MidiMesh.Appearance;

/// <summary>A plain colour, kept free of WinUI so the colour maths can be unit tested.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Moves <paramref name="amount"/> of the way (0 to 1) from this colour to <paramref name="other"/>.</summary>
    public Rgb Mix(Rgb other, double amount) => new(
        Step(R, other.R, amount),
        Step(G, other.G, amount),
        Step(B, other.B, amount));

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    private static byte Step(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);
}

/// <summary>
/// The seven accent shades WinUI uses (SystemAccentColor and its Light1-3 and Dark1-3), plus
/// <see cref="Bright"/>: the colour of the activity lights and the icon on a dark background.
/// </summary>
public sealed record AccentPalette(Rgb Base, Rgb Light1, Rgb Light2, Rgb Light3, Rgb Dark1, Rgb Dark2, Rgb Dark3, Rgb Bright)
{
    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb Black = new(0, 0, 0);

    /// <summary>The green of the icon. Its shades are the ones App.xaml has always used.</summary>
    public static AccentPalette Green { get; } = new(
        Rgb.FromHex("#50DC6E"),
        Rgb.FromHex("#6FE587"), Rgb.FromHex("#8DEDA0"), Rgb.FromHex("#B0F4BE"),
        Rgb.FromHex("#3FB859"), Rgb.FromHex("#2F9445"), Rgb.FromHex("#217032"),
        Rgb.FromHex("#50DC6E"));

    /// <summary>Base colours for the presets: bright enough to read as lights on a dark window.</summary>
    public static IReadOnlyDictionary<AccentChoice, Rgb> PresetColours { get; } = new Dictionary<AccentChoice, Rgb>
    {
        [AccentChoice.Green] = Green.Base,
        [AccentChoice.Blue] = Rgb.FromHex("#4AA8FF"),
        [AccentChoice.Purple] = Rgb.FromHex("#A98BFF"),
        [AccentChoice.Orange] = Rgb.FromHex("#FF9A3C"),
        [AccentChoice.Pink] = Rgb.FromHex("#FF6EB4"),
        [AccentChoice.Teal] = Rgb.FromHex("#2ED3C6"),
    };

    /// <summary>The palette for a preset. Windows has no preset: use <see cref="FromSystem"/>.</summary>
    public static AccentPalette ForPreset(AccentChoice choice)
    {
        if (choice == AccentChoice.Green) return Green;
        if (!PresetColours.TryGetValue(choice, out var colour)) throw new ArgumentOutOfRangeException(nameof(choice));
        return FromBase(colour);
    }

    /// <summary>Shades made by mixing the base colour with white and with black.</summary>
    public static AccentPalette FromBase(Rgb colour) => new(
        colour,
        colour.Mix(White, 0.17), colour.Mix(White, 0.33), colour.Mix(White, 0.5),
        colour.Mix(Black, 0.17), colour.Mix(Black, 0.33), colour.Mix(Black, 0.5),
        colour);

    /// <summary>
    /// The Windows accent, using the shades Windows itself made. The Windows accent is often a deep
    /// colour (the default is #0078D4), so the lights use Light1, as Windows does on dark surfaces.
    /// </summary>
    public static AccentPalette FromSystem(Rgb accent, Rgb light1, Rgb light2, Rgb light3, Rgb dark1, Rgb dark2, Rgb dark3) =>
        new(accent, light1, light2, light3, dark1, dark2, dark3, light1);

    /// <summary>Colour of a lit activity light, and of the filter button when a route has options.</summary>
    public Rgb LitColour(bool dark) => dark ? Bright : Dark1;

    /// <summary>Colour of an idle activity light: a hint of the accent on the background.</summary>
    public Rgb IdleColour(bool dark) => dark
        ? Rgb.FromHex("#363636").Mix(Bright, 0.15)
        : Rgb.FromHex("#C4C4C4").Mix(Dark1, 0.3);

    /// <summary>Every shade, for finding and swapping the accent brushes WinUI has already made.</summary>
    public Rgb[] Shades => [Base, Light1, Light2, Light3, Dark1, Dark2, Dark3];
}

/// <summary>Recolours the green parts of the icon to another accent, leaving the dark parts alone.</summary>
public static class IconTint
{
    /// <summary>
    /// Pixels are BGRA, straight alpha. Each pixel is treated as a mix of a grey and the icon's green:
    /// how far its green channel rises above the other two says how much green it has, and that much
    /// of the difference between the green and the new colour is added. Edges stay smooth.
    /// </summary>
    public static void TintBgra(Span<byte> pixels, Rgb from, Rgb to)
    {
        double fullGreen = from.G - (from.R + from.B) / 2.0;
        if (fullGreen <= 0) return;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            var amount = Math.Clamp((g - (r + b) / 2.0) / fullGreen, 0, 1);
            if (amount == 0) continue;
            pixels[i] = Shift(b, from.B, to.B, amount);
            pixels[i + 1] = Shift(g, from.G, to.G, amount);
            pixels[i + 2] = Shift(r, from.R, to.R, amount);
        }
    }

    private static byte Shift(int value, byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(value + (to - from) * amount), 0, 255);
}
