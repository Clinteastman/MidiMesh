namespace MidiMesh.Midi;

public enum OpenOutcome
{
    /// <summary>The port is open and returned.</summary>
    Opened,

    /// <summary>Windows answered but gave no port (the device is not ready yet).</summary>
    Failed,

    /// <summary>Windows did not answer in time. The request carries on in the background.</summary>
    TimedOut,

    /// <summary>An earlier request that timed out has still not answered, so no new one was made.</summary>
    StillWaiting,
}

/// <summary>
/// Opens one endpoint's port with a time limit.
///
/// Windows sometimes never answers a request to open a MIDI port (seen with the M-VAVE FM-1 input
/// right after the app restarted). The engine opens ports one at a time, so a request that never
/// answers used to hold up every other device, Reconnect all and Quit. Now the engine stops
/// waiting after <see cref="Timeout"/>. The request is kept: asking a device that is not
/// answering again tends to hang as well, so the next try waits for that same request, uses its
/// port if it arrives late, and only asks again once it has finished without one.
///
/// Not thread-safe: the engine only calls it while holding its lock.
/// </summary>
public sealed class PortOpener(TimeSpan timeout)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private Task<IDisposable?>? _pending;
    private string? _pendingDeviceId;

    public TimeSpan Timeout { get; } = timeout;

    /// <summary>
    /// Opens the port for <paramref name="deviceId"/> with <paramref name="request"/>, or picks up
    /// the answer to an earlier request for the same device. A request that throws throws here.
    /// </summary>
    public async Task<(OpenOutcome Outcome, IDisposable? Port)> OpenAsync(string deviceId, Func<string, Task<IDisposable?>> request)
    {
        Task<IDisposable?> open;
        if (_pending is { } pending)
        {
            if (!pending.IsCompleted) return (OpenOutcome.StillWaiting, null);
            var pendingId = _pendingDeviceId;
            _pending = null;
            _pendingDeviceId = null;
            if (pendingId == deviceId && pending.IsCompletedSuccessfully && pending.Result is not null)
            {
                return (OpenOutcome.Opened, pending.Result);
            }
            DisposeWhenDone(pending);
            open = request(deviceId);
        }
        else
        {
            open = request(deviceId);
        }

        if (await Task.WhenAny(open, Task.Delay(Timeout)).ConfigureAwait(false) != open)
        {
            _pending = open;
            _pendingDeviceId = deviceId;
            return (OpenOutcome.TimedOut, null);
        }
        var port = await open.ConfigureAwait(false);
        return port is null ? (OpenOutcome.Failed, null) : (OpenOutcome.Opened, port);
    }

    /// <summary>Drops a request that timed out, closing its port if one arrives. Used when the port is closed.</summary>
    public void Discard()
    {
        if (_pending is not { } pending) return;
        _pending = null;
        _pendingDeviceId = null;
        DisposeWhenDone(pending);
    }

    private static void DisposeWhenDone(Task<IDisposable?> open) =>
        open.ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return; // Also observes a failure, so it is not reported as unobserved.
            try
            {
                t.Result?.Dispose();
            }
            catch
            {
                // Closing a port nobody uses; nothing to do if it fails.
            }
        }, TaskScheduler.Default);
}
