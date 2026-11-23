using MidiMesh.Routing;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace MidiMesh.Midi;

/// <summary>
/// Watches MIDI devices, keeps their ports open and forwards messages along the routes.
///
/// Reconnecting is the main job:
/// - A DeviceWatcher per direction reports plug and unplug. Unplug closes the port; plug opens it
///   again under the same name, so the routes (stored by name) carry straight on.
/// - A health timer retries ports that failed to open and restarts a watcher that died.
/// - Opening a port has a time limit (see PortOpener), so one device Windows does not answer for
///   cannot hold up the others, Reconnect all or Quit.
/// - After sleep, ports can look open but pass nothing, so on resume everything is closed and
///   reopened from scratch.
/// </summary>
public sealed class MidiEngine : IDisposable
{
    private static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(3);

    /// <summary>A health tick this late means the PC was asleep.</summary>
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, MidiEndpoint> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MidiEndpoint> _outputs = new(StringComparer.Ordinal);
    private readonly Dictionary<Route, RouteOptions> _routes = [];

    /// <summary>Input name to its outputs and their options. Replaced whole on change; read on the MIDI thread.</summary>
    private volatile Dictionary<string, RouteTarget[]> _routeMap = new(StringComparer.Ordinal);

    /// <summary>One output of an input's routes, with that route's options.</summary>
    private sealed record RouteTarget(MidiEndpoint Endpoint, RouteOptions Options);

    private DeviceWatcher? _inWatcher;
    private DeviceWatcher? _outWatcher;
    private Timer? _healthTimer;
    private DateTime _lastHealthTick = DateTime.UtcNow;
    private bool _disposed;
    private int _watchersScanned;
    private readonly TaskCompletionSource _initialScan = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Raised (on a background thread) when a device appears, goes or changes state.</summary>
    public event Action? EndpointsChanged;

    /// <summary>Completes once both device lists have loaded and their ports have been tried, so the window can open at the right size.</summary>
    public Task InitialScan => _initialScan.Task;

    public int RouteCount
    {
        get { lock (_routes) return _routes.Count; }
    }

    public void Start(IEnumerable<KeyValuePair<Route, RouteOptions>> savedRoutes)
    {
        foreach (var (route, options) in savedRoutes)
        {
            GetOrAdd(PortKind.Input, route.From);
            GetOrAdd(PortKind.Output, route.To);
            _routes[route] = options;
        }
        RebuildRouteMap();
        Log.Info($"Started with {_routes.Count} saved route(s).");

        StartWatchers();
        _lastHealthTick = DateTime.UtcNow;
        _healthTimer = new Timer(_ => _ = HealthCheckAsync(), null, HealthInterval, HealthInterval);
    }

    public IReadOnlyList<MidiEndpoint> Inputs => Snapshot(_inputs);

    public IReadOnlyList<MidiEndpoint> Outputs => Snapshot(_outputs);

    public bool IsRouted(string from, string to)
    {
        lock (_routes) return _routes.ContainsKey(new Route(from, to));
    }

    public void SetRoute(string from, string to, bool on)
    {
        var route = new Route(from, to);
        bool changed;
        lock (_routes) changed = on ? _routes.TryAdd(route, RouteOptions.Default) : _routes.Remove(route);
        if (!changed) return;

        Log.Info($"Route {(on ? "on" : "off")}: {from} -> {to}");
        RouteChanged();
    }

    /// <summary>The route's options, or null when there is no such route.</summary>
    public RouteOptions? GetRouteOptions(string from, string to)
    {
        lock (_routes) return _routes.TryGetValue(new Route(from, to), out var options) ? options : null;
    }

    /// <summary>Changes the filters and channels of an existing route. Takes effect on the next message.</summary>
    public void SetRouteOptions(string from, string to, RouteOptions options)
    {
        var route = new Route(from, to);
        lock (_routes)
        {
            if (!_routes.TryGetValue(route, out var old) || old == options) return;
            _routes[route] = options;
        }
        Log.Info($"Route options {from} -> {to}: {options.Describe()}");
        RouteChanged();
    }

    private void RouteChanged()
    {
        RebuildRouteMap();
        List<KeyValuePair<Route, RouteOptions>> copy;
        lock (_routes) copy = [.. _routes];
        RouteStore.Save(copy);
    }

    /// <summary>Sends all-notes-off and all-sound-off on every channel to every routed output. Clears stuck notes.</summary>
    public int Panic()
    {
        var targets = _routeMap.Values.SelectMany(t => t).Select(t => t.Endpoint).Distinct().ToList();
        foreach (var target in targets) SendAllNotesOff(target, allSoundOff: true);
        Log.Info($"Panic: all notes off sent to {targets.Count} output(s).");
        return targets.Count;
    }

    private static void SendAllNotesOff(MidiEndpoint target, bool allSoundOff)
    {
        lock (target.SendLock)
        {
            var port = target.OutPort;
            if (port is null) return;
            try
            {
                for (byte channel = 0; channel < 16; channel++)
                {
                    port.SendMessage(new MidiControlChangeMessage(channel, 64, 0));  // sustain pedal up
                    port.SendMessage(new MidiControlChangeMessage(channel, 123, 0)); // all notes off
                    if (allSoundOff) port.SendMessage(new MidiControlChangeMessage(channel, 120, 0));
                }
            }
            catch (Exception ex)
            {
                Log.Info($"All notes off to {target} failed: {ex.Message}");
            }
        }
    }

    /// <summary>Closes every port and finds all devices again. Used after sleep and by the Reconnect button.</summary>
    public async Task ReconnectAllAsync(string reason)
    {
        Log.Info($"Reconnecting all ports ({reason}).");
        await _gate.WaitAsync();
        try
        {
            StopWatchers();
            foreach (var endpoint in AllEndpoints())
            {
                ClosePort(endpoint);
                endpoint.DeviceId = null;
            }
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
        StartWatchers();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _healthTimer?.Dispose();
        StopWatchers();
        // An open in progress can hold the lock for up to the open time limit. Do not wait longer
        // than that: quitting must never hang, and the ports close with the process anyway.
        var locked = _gate.Wait(PortOpener.DefaultTimeout + TimeSpan.FromSeconds(1));
        try
        {
            foreach (var endpoint in AllEndpoints()) ClosePort(endpoint);
        }
        finally
        {
            if (locked) _gate.Release();
        }
    }

    // ---- Watchers -------------------------------------------------------------------------

    private void StartWatchers()
    {
        _inWatcher = CreateWatcher(PortKind.Input, MidiInPort.GetDeviceSelector());
        _outWatcher = CreateWatcher(PortKind.Output, MidiOutPort.GetDeviceSelector());
    }

    private DeviceWatcher CreateWatcher(PortKind kind, string selector)
    {
        var watcher = DeviceInformation.CreateWatcher(selector);
        watcher.Added += (w, info) => _ = OnDeviceAddedAsync(w, kind, info.Id, info.Name);
        watcher.Removed += (w, update) => _ = OnDeviceRemovedAsync(w, kind, update.Id);
        watcher.Updated += (_, _) => { };
        watcher.EnumerationCompleted += (w, _) =>
        {
            if (IsCurrent(w)) Log.Info($"{kind} device list loaded.");
            _ = MarkScannedAsync();
        };
        watcher.Stopped += (w, _) =>
        {
            if (IsCurrent(w)) Log.Info($"{kind} watcher stopped ({w.Status}); the health check will restart it.");
        };
        watcher.Start();
        return watcher;
    }

    private void StopWatchers()
    {
        foreach (var watcher in new[] { _inWatcher, _outWatcher })
        {
            if (watcher is null) continue;
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Info($"Watcher stop failed: {ex.Message}");
            }
        }
        _inWatcher = null;
        _outWatcher = null;
    }

    /// <summary>Waits its turn behind the queued opens, so InitialScan completes after the ports were tried.</summary>
    private async Task MarkScannedAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
        if (Interlocked.Increment(ref _watchersScanned) >= 2) _initialScan.TrySetResult();
    }

    /// <summary>Events from a watcher that has since been replaced are ignored.</summary>
    private bool IsCurrent(DeviceWatcher watcher) =>
        ReferenceEquals(watcher, _inWatcher) || ReferenceEquals(watcher, _outWatcher);

    private async Task OnDeviceAddedAsync(DeviceWatcher watcher, PortKind kind, string id, string name)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsCurrent(watcher) || _disposed) return;
            var table = Table(kind);
            MidiEndpoint endpoint;
            lock (table)
            {
                if (table.Values.Any(e => e.DeviceId == id)) return;
                endpoint = GetOrAdd(kind, UniqueName(table, name, id));
                endpoint.DeviceId = id;
            }
            Log.Info($"Found {endpoint}.");
            await OpenPortAsync(endpoint);
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    private async Task OnDeviceRemovedAsync(DeviceWatcher watcher, PortKind kind, string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsCurrent(watcher)) return;
            var table = Table(kind);
            MidiEndpoint? endpoint;
            lock (table) endpoint = table.Values.FirstOrDefault(e => e.DeviceId == id);
            if (endpoint is null) return;
            Log.Info($"Lost {endpoint} (unplugged or switched off).");
            ClosePort(endpoint);
            endpoint.DeviceId = null;

            // An input unplugged mid-note never sends its note-offs, so release them on its outputs.
            if (kind == PortKind.Input && _routeMap.TryGetValue(endpoint.Name, out var targets))
            {
                foreach (var target in targets) SendAllNotesOff(target.Endpoint, allSoundOff: false);
            }
        }
        finally
        {
            _gate.Release();
        }
        RaiseChanged();
    }

    /// <summary>
    /// Two devices with the same name get "Name #2" so each has its own row. The first one keeps
    /// the plain name, so a single device's routes always match. Call with the table locked.
    /// </summary>
    private static string UniqueName(Dictionary<string, MidiEndpoint> table, string name, string id)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Unnamed MIDI device";
        var candidate = name;
        for (var n = 2; table.TryGetValue(candidate, out var existing) && existing.DeviceId is not null && existing.DeviceId != id; n++)
        {
            candidate = $"{name} #{n}";
        }
        return candidate;
    }

    // ---- Ports ----------------------------------------------------------------------------

    private async Task OpenPortAsync(MidiEndpoint endpoint)
    {
        if (endpoint.IsOpen || endpoint.DeviceId is null) return;
        var id = endpoint.DeviceId;
        try
        {
            var (outcome, port) = await endpoint.Opener.OpenAsync(id, deviceId => RequestPortAsync(endpoint.Kind, deviceId));
            switch (outcome)
            {
                case OpenOutcome.StillWaiting:
                    return;
                case OpenOutcome.TimedOut:
                    Log.Info($"{endpoint} did not open within {endpoint.Opener.Timeout.TotalSeconds:0}s; still waiting for Windows.");
                    return;
                case OpenOutcome.Failed:
                    Log.Info($"Could not open {endpoint} yet; will retry.");
                    return;
            }
            if (port is MidiInPort inPort)
            {
                inPort.MessageReceived += (sender, args) => OnMessage(endpoint, sender, args.Message);
                endpoint.InPort = inPort;
            }
            else
            {
                endpoint.OutPort = (IMidiOutPort)port!;
            }
            Log.Info($"Opened {endpoint}.");
        }
        catch (Exception ex)
        {
            Log.Info($"Error opening {endpoint}: {ex.Message}; will retry.");
        }
    }

    private static async Task<IDisposable?> RequestPortAsync(PortKind kind, string id) =>
        kind == PortKind.Input ? await MidiInPort.FromIdAsync(id) : await MidiOutPort.FromIdAsync(id);

    private static void ClosePort(MidiEndpoint endpoint)
    {
        endpoint.Opener.Discard();
        try
        {
            endpoint.InPort?.Dispose();
            lock (endpoint.SendLock) endpoint.OutPort?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Info($"Error closing {endpoint}: {ex.Message}");
        }
        endpoint.InPort = null;
        lock (endpoint.SendLock) endpoint.OutPort = null;
    }

    // ---- Routing (runs on the MIDI callback thread) ---------------------------------------

    private void OnMessage(MidiEndpoint source, MidiInPort sender, IMidiMessage message)
    {
        if (!ReferenceEquals(sender, source.InPort)) return;
        var visible = ShowsActivity(message.Type);
        if (visible)
        {
            source.MarkActivity();
            source.CountMessage();
        }
        if (!_routeMap.TryGetValue(source.Name, out var targets)) return;

        IBuffer data = message.RawData;
        byte[]? bytes = null;
        foreach (var (target, options) in targets)
        {
            // Fast path: a route with default options forwards the buffer untouched.
            var send = data;
            if (!options.IsDefault)
            {
                bytes ??= data.ToArray();
                var filtered = RouteFilter.Apply(options, bytes);
                if (filtered is null) continue;
                if (!ReferenceEquals(filtered, bytes)) send = filtered.AsBuffer();
            }

            lock (target.SendLock)
            {
                var port = target.OutPort;
                if (port is null) continue;
                try
                {
                    port.SendBuffer(send);
                }
                catch (Exception ex)
                {
                    // A dead port; the watcher or health check will bring it back.
                    Log.Info($"Send to {target} failed: {ex.Message}");
                    continue;
                }
            }
            if (visible)
            {
                target.MarkActivity();
                target.CountMessage();
            }
        }
    }

    /// <summary>
    /// Only channel messages (notes, CC, pitch bend and so on) light the lamps. Clock, active
    /// sensing and SysEx can stream all the time (the FM-1 sends SysEx constantly), so they would
    /// keep the lamp lit and hide whether notes are getting through. They are still forwarded.
    /// </summary>
    private static bool ShowsActivity(MidiMessageType type) =>
        type is MidiMessageType.NoteOff or MidiMessageType.NoteOn or MidiMessageType.PolyphonicKeyPressure
            or MidiMessageType.ControlChange or MidiMessageType.ProgramChange or MidiMessageType.ChannelPressure
            or MidiMessageType.PitchBendChange;

    private void RebuildRouteMap()
    {
        List<KeyValuePair<Route, RouteOptions>> routes;
        lock (_routes) routes = [.. _routes];
        var map = new Dictionary<string, RouteTarget[]>(StringComparer.Ordinal);
        foreach (var group in routes.GroupBy(r => r.Key.From))
        {
            GetOrAdd(PortKind.Input, group.Key);
            map[group.Key] = group.Select(r => new RouteTarget(GetOrAdd(PortKind.Output, r.Key.To), r.Value)).ToArray();
        }
        _routeMap = map;
    }

    // ---- Health check ---------------------------------------------------------------------

    private async Task HealthCheckAsync()
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        var gap = now - _lastHealthTick;
        _lastHealthTick = now;
        if (gap > SleepGap)
        {
            await ReconnectAllAsync($"woke up after {gap.TotalSeconds:0}s");
            return;
        }

        LogTraffic();
        if (!await _gate.WaitAsync(0)) return;
        var changed = false;
        try
        {
            if (WatcherDead(_inWatcher) || WatcherDead(_outWatcher))
            {
                Log.Info("A device watcher died; restarting.");
                StopWatchers();
                foreach (var endpoint in AllEndpoints())
                {
                    ClosePort(endpoint);
                    endpoint.DeviceId = null;
                }
                StartWatchers();
                changed = true;
            }
            else
            {
                foreach (var endpoint in AllEndpoints().Where(e => e.IsPresent && !e.IsOpen))
                {
                    await OpenPortAsync(endpoint);
                    changed |= endpoint.IsOpen;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
        if (changed) RaiseChanged();
    }

    /// <summary>One log line per health tick while notes are flowing, so a silent route shows up in the log.</summary>
    private void LogTraffic()
    {
        var parts = AllEndpoints()
            .Select(e => (e, n: e.TakeNewMessages()))
            .Where(x => x.n > 0)
            .Select(x => $"{x.e.Name} {(x.e.Kind == PortKind.Input ? "in" : "out")} {x.n}")
            .ToList();
        if (parts.Count > 0) Log.Info("Traffic: " + string.Join(", ", parts));
    }

    private static bool WatcherDead(DeviceWatcher? watcher) =>
        watcher is null || watcher.Status is DeviceWatcherStatus.Aborted or DeviceWatcherStatus.Stopped;

    // ---- Helpers --------------------------------------------------------------------------

    private Dictionary<string, MidiEndpoint> Table(PortKind kind) => kind == PortKind.Input ? _inputs : _outputs;

    private MidiEndpoint GetOrAdd(PortKind kind, string name)
    {
        var table = Table(kind);
        lock (table)
        {
            if (!table.TryGetValue(name, out var endpoint))
            {
                endpoint = new MidiEndpoint(kind, name);
                table[name] = endpoint;
            }
            return endpoint;
        }
    }

    private List<MidiEndpoint> AllEndpoints()
    {
        var all = new List<MidiEndpoint>();
        lock (_inputs) all.AddRange(_inputs.Values);
        lock (_outputs) all.AddRange(_outputs.Values);
        return all;
    }

    private static List<MidiEndpoint> Snapshot(Dictionary<string, MidiEndpoint> table)
    {
        lock (table) return [.. table.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private void RaiseChanged() => EndpointsChanged?.Invoke();
}
