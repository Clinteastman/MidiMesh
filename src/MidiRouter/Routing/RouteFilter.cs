namespace MidiRouter.Routing;

/// <summary>
/// Applies a route's options to one raw MIDI message. Pure byte logic with no Windows types, so
/// it is unit tested on its own (tests/MidiRouter.Tests).
/// </summary>
public static class RouteFilter
{
    /// <summary>
    /// Returns the bytes to send: <paramref name="message"/> itself when unchanged, a changed copy
    /// when the channel is remapped, or null when the route drops the message.
    /// </summary>
    public static byte[]? Apply(RouteOptions options, byte[] message)
    {
        if (message.Length == 0) return null;
        if (options.IsDefault) return message;

        var status = message[0];
        if ((options.Block & KindOf(status)) != MessageKind.None) return null;
        if (status is < 0x80 or >= 0xF0) return message; // System messages have no channel.

        var channel = (status & 0x0F) + 1;
        if (options.FromChannel is int from && channel != from) return null;
        if (options.ToChannel is int to && to != channel)
        {
            var copy = (byte[])message.Clone();
            copy[0] = (byte)((status & 0xF0) | (to - 1));
            return copy;
        }
        return message;
    }

    /// <summary>The blockable kind of a message, from its status byte. Tune request, active sensing and reset are None.</summary>
    public static MessageKind KindOf(byte status) => status switch
    {
        >= 0x80 and < 0xA0 => MessageKind.Notes, // note off, note on
        >= 0xA0 and < 0xB0 => MessageKind.Aftertouch, // polyphonic key pressure
        >= 0xB0 and < 0xC0 => MessageKind.ControlChange,
        >= 0xC0 and < 0xD0 => MessageKind.ProgramChange,
        >= 0xD0 and < 0xE0 => MessageKind.Aftertouch, // channel pressure
        >= 0xE0 and < 0xF0 => MessageKind.PitchBend,
        0xF0 => MessageKind.SysEx,
        0xF1 or 0xF2 or 0xF3 => MessageKind.Clock, // time code, song position, song select
        0xF8 or 0xFA or 0xFB or 0xFC => MessageKind.Clock, // clock, start, continue, stop
        _ => MessageKind.None, // tune request, active sensing, reset
    };
}
