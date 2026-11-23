using Windows.Devices.Midi;

namespace MidiMesh.Midi;

public enum PortKind
{
    Input,
    Output,
}

/// <summary>
/// One MIDI input or output, known by name. The object lives for the whole run, even while the
/// device is unplugged, so routes can point at it and pick it up again when it comes back.
/// </summary>
public sealed class MidiEndpoint
{
    private long _lastActivity;
    private long _messages;
    private long _messagesLogged;

    public MidiEndpoint(PortKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public PortKind Kind { get; }

    public string Name { get; }

    /// <summary>The Windows device id the port is open on (or waiting to open on).</summary>
    public string? DeviceId { get; internal set; }

    internal MidiInPort? InPort { get; set; }

    internal IMidiOutPort? OutPort { get; set; }

    /// <summary>Serialises sends when two inputs feed the same output at once.</summary>
    internal object SendLock { get; } = new();

    public bool IsOpen => InPort is not null || OutPort is not null;

    /// <summary>The device is plugged in (Windows lists it), whether or not the port opened yet.</summary>
    public bool IsPresent => DeviceId is not null;

    /// <summary>Environment.TickCount64 of the last channel message, for the activity light.</summary>
    public long LastActivity => Interlocked.Read(ref _lastActivity);

    internal void MarkActivity() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    /// <summary>Messages received (input) or sent (output), channel messages only (see MidiEngine.ShowsActivity).</summary>
    public long Messages => Interlocked.Read(ref _messages);

    internal void CountMessage() => Interlocked.Increment(ref _messages);

    /// <summary>Messages since the last call, for the traffic line in the log.</summary>
    internal long TakeNewMessages()
    {
        var total = Messages;
        var fresh = total - _messagesLogged;
        _messagesLogged = total;
        return fresh;
    }

    public override string ToString() => $"{Kind} '{Name}'";
}
