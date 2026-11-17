# MIDI Router

A small, free Windows 11 app that routes MIDI from one device to another, for example a
KeyStep 37 mk2 into an M-VAVE FM-1. It replaces MIDIberry, which loses devices after a replug or
sleep.

## What it does

- Lists every MIDI input and output Windows can see.
- Grid: inputs down the side, outputs across the top. Tick a box to send that input to that
  output. Any input can go to many outputs, and many inputs can feed one output.
- Routes are saved by device **name**, not device id, in
  `%LOCALAPPDATA%\MidiRouter\routes.json`. They come back when you reopen the app.
- Reconnects by itself:
  - Unplug a device and its row shows "unplugged". Plug it back in and its port reopens and
    its routes carry on. Nothing to click.
  - After the PC wakes from sleep, every port is closed and reopened, because a port can look
    open after sleep but pass nothing.
  - A check every 3 seconds retries ports that failed to open and restarts the device watcher
    if Windows stops it.
  - **Reconnect** does the same full reopen by hand.
- A light next to each device flashes when notes, CC, pitch bend and other channel messages pass.
  Clock, active sensing and SysEx are still routed but do not light the lamp, because some
  devices (the FM-1 included) send them all the time.
- **Panic** sends all-notes-off and all-sound-off on every channel to every routed output, to
  clear stuck notes. The same happens by itself (all-notes-off only) on an input's outputs when
  that input is unplugged, since it can no longer send its own note-offs.
- Lives in the tray. Closing the window hides it and routing carries on. Click the tray icon to
  open it again; right-click for Panic, Reconnect and Quit. **Quit** (tray menu or the `...`
  menu) is the only thing that stops routing.
- **Start with Windows** (in the `...` menu, off by default) starts it in the tray at sign-in.
  It points at the exe you turned it on from, so turn it on again if you move the exe.
- The window opens sized to fit the grid: small for two devices, bigger for many, never more than
  90% of the screen (the grid scrolls past that). It stops resizing itself once you drag its edge.
- Only one copy runs at a time. Starting it again brings the open window to the front, so notes
  are never sent twice.

## Build and run

Needs the .NET 10 SDK on Windows 10 1904x or later (built and tested on Windows 11).

```bash
cd src/MidiRouter
dotnet run
```

Or build and start the exe:

```bash
dotnet build src/MidiRouter
```

The exe lands in `src/MidiRouter/bin/Debug/net10.0-windows10.0.22621.0/win-x64/MidiRouter.exe`.
The Windows App SDK is bundled (self-contained), so nothing else needs installing.

## Files

| File | What it is |
| --- | --- |
| `%LOCALAPPDATA%\MidiRouter\routes.json` | Saved routes, by device name. Safe to edit while the app is closed. |
| `%LOCALAPPDATA%\MidiRouter\log.txt` | Plug, unplug, open and error events, plus a "Traffic" line every 3 s while notes flow. |

Set `MIDIROUTER_DATA_DIR` to use another folder, for example to test without touching your routes.

## How it works

- `Midi/MidiEngine.cs` uses the in-box WinRT `Windows.Devices.Midi` API (`MidiInPort`,
  `MidiOutPort`) with a `DeviceWatcher` for inputs and one for outputs. Each device is kept as a
  `MidiEndpoint` by name for the whole run, so routes keep pointing at it while it is unplugged.
- Incoming messages are forwarded raw (`SendBuffer`) on the MIDI callback thread, using a route
  map that is swapped whole when routes change, so there is no UI or lock in the note path apart
  from a small per-output send lock.
- `Routing/RouteStore.cs` loads and saves `routes.json`.
- `MainWindow.xaml.cs` draws the title bar and grid, sizes the window and polls the activity
  lights 25 times a second.
- `TrayIcon.cs` is the notification-area icon and menu (plain Win32 `Shell_NotifyIcon`).
- `StartupRegistration.cs` is the "Start with Windows" value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Channel or CC remapping is not in v1. The place for it is the `Route` record (per-route options)
and `MidiEngine.OnMessage` (transform the bytes before `SendBuffer`).

## Notes

- With Windows MIDI Services running (the `MidiSrv` service, on by default in recent Windows 11
  builds) ports are shared, so this app can use the KeyStep while Ableton uses it too. On older
  Windows without it, an output port can only be opened by one app at a time, and this app opens
  them all.
- Two devices with the same name show as `Name` and `Name #2`.
- Device names are whatever the device reports to Windows. The FM-1 shows as `M-VAVE FM-1` on
  its current firmware. If a firmware update renames a device, its old routes show as unplugged:
  tick the new name, or edit `routes.json` with the app closed.
