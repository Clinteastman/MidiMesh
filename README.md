# MidiMesh

A small, free MIDI router for Windows 11. Send any MIDI input to any MIDI output, for example a
KeyStep 37 mk2 into an M-VAVE FM-1, and it keeps working when you unplug a device, plug it back
in, or wake the PC from sleep. It was written to replace MIDIberry, which loses devices after a
replug or sleep.

<!-- TODO screenshot: add docs/screenshot.png (the routing grid) and replace this comment with:
![MidiMesh routing grid](docs/screenshot.png) -->
_Screenshot coming soon._

## Download and install

Get the latest version from the [Releases page](https://github.com/Clinteastman/midimesh/releases/latest).

| File | What it is |
| --- | --- |
| `MidiMesh-win-x64-Setup.exe` | Installer for most PCs (Intel and AMD). |
| `MidiMesh-win-arm64-Setup.exe` | Installer for Arm PCs (Snapdragon). |
| `MidiMesh-win-x64-Portable.zip` | No installer: unzip anywhere and run `MidiMesh.exe`. |
| `MidiMesh-win-arm64-Portable.zip` | The same for Arm PCs. |

The installer needs no admin rights. It installs for your user only, into
`%LOCALAPPDATA%\MidiMesh`, adds Start menu and desktop shortcuts, and starts the app. Uninstall it
from Settings > Apps like any other app.

### "Windows protected your PC"

MidiMesh is not code-signed (a signing certificate costs money every year), so the first time you
run the installer Windows SmartScreen may show "Windows protected your PC". Click **More info**,
then **Run anyway**. If you would rather check first, the full source is here and every release is
built by GitHub Actions from a tagged commit.

### Updates

MidiMesh checks for a new version a minute after it starts and then once a day. A new version
downloads in the background, and MidiMesh then shows **Restart now**. It never restarts by itself,
because restarting pauses the routing for a few seconds. You can also use **Check for updates** in
the `...` menu. Updates are small: usually only the changed files are downloaded.

The portable zip updates itself the same way.

## What it does

- Lists every MIDI input and output Windows can see.
- Grid: inputs down the side, outputs across the top. Tick a box to send that input to that
  output. Any input can go to many outputs, and many inputs can feed one output.
- Routes are saved by device **name**, not device id, so they come back when you reopen the app.
- Reconnects by itself:
  - Unplug a device and its row shows "unplugged". Plug it back in and its port reopens and its
    routes carry on. Nothing to click.
  - After the PC wakes from sleep, every port is closed and reopened, because a port can look open
    after sleep but pass nothing.
  - A check every 3 seconds retries ports that failed to open and restarts the device watcher if
    Windows stops it.
  - **Reconnect** does the same full reopen by hand.
- Each ticked box gets a small filter button. It opens that route's options:
  - **Let through**: untick notes, CC (knobs), pitch bend, aftertouch, program change, clock and
    start/stop, or SysEx to block them on that route only. For example, untick Notes to use a
    controller for its knobs only, or untick SysEx and Clock to keep a busy device quiet.
  - **Take from**: only pass one channel; other channels are dropped.
  - **Send on**: move channel messages to another channel.

  The button turns green when a route has options set; hover over it for a summary.
- A light next to each device flashes when notes, CC, pitch bend and other channel messages pass.
  Clock, active sensing and SysEx are still routed but do not light the lamp, because some devices
  (the FM-1 included) send them all the time.
- **Panic** sends all-notes-off and all-sound-off on every channel to every routed output, to clear
  stuck notes. The same happens by itself (all-notes-off only) on an input's outputs when that
  input is unplugged, since it can no longer send its own note-offs.
- Lives in the tray. Closing the window hides it and routing carries on. Click the tray icon to
  open it again; right-click for Panic, Reconnect and Quit. **Quit** (tray menu or the `...` menu)
  is the only thing that stops routing.
- **Start with Windows** (in the `...` menu, off by default) starts it in the tray at sign-in.
- The window opens sized to fit the grid, never more than 90% of the screen (the grid scrolls past
  that). It stops resizing itself once you drag its edge.
- Only one copy runs at a time. Starting it again brings the open window to the front, so notes
  are never sent twice.
- The version number is at the bottom of the `...` menu.

## Your settings

| File | What it is |
| --- | --- |
| `%APPDATA%\MidiMesh\routes.json` | Saved routes, by device name, with any options (`block`, `fromChannel`, `toChannel`). Safe to edit while the app is closed. |
| `%APPDATA%\MidiMesh\log.txt` | Plug, unplug, open, update and error events, plus a "Traffic" line every 3 s while notes flow. `...` > **Open log folder** opens it. |

Settings are kept apart from the app, so uninstalling or updating never removes your routes.

Coming from the earlier "MIDI Router" build? The first start copies your routes and log from
`%LOCALAPPDATA%\MidiRouter` and moves Start with Windows across. The old files are left in place.

## Notes

- With Windows MIDI Services running (the `MidiSrv` service, on by default in recent Windows 11
  builds) ports are shared, so MidiMesh can use a keyboard while a DAW such as Ableton uses it too.
  On older Windows without it, an output port can only be opened by one app at a time, and
  MidiMesh opens them all.
- Two devices with the same name show as `Name` and `Name #2`.
- Device names are whatever the device reports to Windows. If a firmware update renames a device,
  its old routes show as unplugged: tick the new name, or edit `routes.json` with the app closed.

## Build from source

Needs Windows 10 (1904x) or later and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
Built and tested on Windows 11.

```bash
git clone https://github.com/Clinteastman/midimesh.git
cd midimesh
dotnet run --project src/MidiMesh
```

The Windows App SDK is bundled (self-contained), so nothing else needs installing. A build lands in
`src/MidiMesh/bin/Debug/net10.0-windows10.0.22621.0/win-x64/MidiMesh.exe`.

Run the tests (routing filters, channel changes, the routes file, the move from the old name):

```bash
cd tests/MidiMesh.Tests
dotnet test
```

To try a build without touching your real routes, set `MIDIMESH_DATA_DIR` to another folder
first. That copy then also runs alongside your normal one and keeps its own Start with Windows
setting.

### Make the installer locally

```powershell
./build/pack.ps1 -Version 0.1.0
```

This publishes x64 and Arm64 builds and uses [Velopack](https://velopack.io) to make the installers,
portable zips and update files in `artifacts/releases`. To test updates against a local folder
instead of GitHub, start the installed app with `MIDIMESH_UPDATE_URL` set to that folder.

### Releases

Pushing a tag like `v0.2.0` runs `.github/workflows/release.yml`, which tests, builds and packs
both CPU types and publishes a GitHub Release with the files. Installed copies pick it up from
there. A tag with a dash, like `v0.2.0-beta.1`, becomes a pre-release, which installed copies
ignore. The version in `src/MidiMesh/MidiMesh.csproj` is used for local builds; releases take it
from the tag.

## How it works

- `Midi/MidiEngine.cs` uses the in-box WinRT `Windows.Devices.Midi` API (`MidiInPort`,
  `MidiOutPort`) with a `DeviceWatcher` for inputs and one for outputs. Each device is kept as a
  `MidiEndpoint` by name for the whole run, so routes keep pointing at it while it is unplugged.
- Incoming messages are forwarded raw (`SendBuffer`) on the MIDI callback thread, using a route map
  that is swapped whole when routes change, so there is no UI or lock in the note path apart from a
  small per-output send lock.
- `Routing/RouteOptions.cs` and `Routing/RouteFilter.cs` hold a route's options and apply them to
  each raw message. Routes with no options skip this and forward the buffer untouched.
- `Routing/RouteStore.cs` loads and saves `routes.json`.
- `MainWindow.xaml.cs` draws the title bar and grid, sizes the window and polls the activity
  lights 25 times a second.
- `TrayIcon.cs` is the notification-area icon and menu (plain Win32 `Shell_NotifyIcon`).
- `StartupRegistration.cs` is the "Start with Windows" value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- `Updater.cs` checks GitHub Releases through Velopack; `Program.cs` hands installer and
  uninstaller events to Velopack first thing.
- `AppInfo.cs` holds the product name; the version and repository address come from the csproj.

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

Copyright (C) 2026 Clinteastman. MidiMesh is free software under the
[GNU General Public License v3.0 or later](LICENSE) (GPL-3.0-or-later). You can use, share and
change it; if you share a changed version, you must share its source under the same licence.

[NOTICE.md](NOTICE.md) has the copyright notice, an extra permission that allows release builds
to bundle the Windows App SDK and .NET runtime, and the licences of the bundled components.
