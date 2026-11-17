# Contributing to MidiMesh

Thanks for helping. MidiMesh is a small app with one job: route MIDI reliably. Changes that keep it
small, reliable and easy to understand are the most welcome.

## Reporting a bug

Open an issue with the **Bug report** form. The most useful things to include:

- Your Windows version and MidiMesh version (bottom of the `...` menu).
- The MIDI devices involved, by the names MidiMesh shows.
- What you did, what you expected, and what happened.
- The relevant part of the log: `...` > **Open log folder** > `log.txt`.

## Suggesting a feature

Open an issue with the **Feature request** form and describe the problem you want solved, not just
the feature. A short example of your setup (which device into which) helps a lot.

## Making a change

1. Fork the repo and make a branch from `main`.
2. Install the .NET 10 SDK, then build and run:

   ```bash
   dotnet run --project src/MidiMesh
   ```

   Set `MIDIMESH_DATA_DIR` to a scratch folder while testing, so your own routes and Start with
   Windows setting are left alone.
3. Keep changes focused. Match the existing style; no reformatting of code you did not change.
4. Add or update tests in `tests/MidiMesh.Tests` for routing logic, and run them:

   ```bash
   cd tests/MidiMesh.Tests
   dotnet test
   ```

5. Try it with real MIDI devices if your change touches routing, reconnecting or the tray.
6. Open a pull request saying what changed, why, and how you tested it.

CI builds the app for x64 and Arm64 and runs the tests on every pull request.

## Code layout

See "How it works" in the [README](README.md).

## Licence

By contributing you agree that your contribution is licensed under the project's
[MIT licence](LICENSE).
