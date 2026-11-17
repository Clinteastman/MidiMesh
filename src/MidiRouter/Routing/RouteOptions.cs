namespace MidiRouter.Routing;

/// <summary>Kinds of MIDI message a route can block.</summary>
[Flags]
public enum MessageKind
{
    None = 0,

    /// <summary>Control change: mod wheel, sustain, knobs.</summary>
    ControlChange = 1,

    PitchBend = 2,

    /// <summary>Channel pressure and polyphonic key pressure.</summary>
    Aftertouch = 4,

    ProgramChange = 8,

    /// <summary>Timing clock, start, stop, continue, song position, song select and MIDI time code.</summary>
    Clock = 16,

    SysEx = 32,

    /// <summary>Note on and note off, for a controller used only for its knobs.</summary>
    Notes = 64,
}

/// <summary>
/// Per-route settings. The default lets everything through unchanged. Channels are 1 to 16, as
/// shown on devices; null means "all" for <see cref="FromChannel"/> and "same as it came in" for
/// <see cref="ToChannel"/>.
/// </summary>
public sealed record RouteOptions
{
    public static readonly RouteOptions Default = new();

    public MessageKind Block { get; init; }

    /// <summary>Only channel messages on this channel get through. Other channels are dropped.</summary>
    public int? FromChannel { get; init; }

    /// <summary>Channel messages are sent on this channel instead of their own.</summary>
    public int? ToChannel { get; init; }

    public bool IsDefault => Block == MessageKind.None && FromChannel is null && ToChannel is null;

    /// <summary>Short text for a tooltip, for example "Blocks SysEx, Clock. Channel 1 to 2."</summary>
    public string Describe()
    {
        if (IsDefault) return "Everything gets through unchanged.";
        var parts = new List<string>();
        if (Block != MessageKind.None) parts.Add($"Blocks {Block.ToString().Replace("ControlChange", "CC").Replace("PitchBend", "pitch bend").Replace("ProgramChange", "program change")}.");
        if (FromChannel is not null || ToChannel is not null)
        {
            var from = FromChannel is int f ? $"channel {f}" : "all channels";
            var to = ToChannel is int t ? $"channel {t}" : "same channel";
            parts.Add($"From {from}, sent on {to}.");
        }
        return string.Join(" ", parts);
    }
}
