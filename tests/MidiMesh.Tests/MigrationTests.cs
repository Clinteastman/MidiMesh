using Microsoft.Win32;
using Xunit;

namespace MidiMesh.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MidiMesh.Tests." + Guid.NewGuid().ToString("N"));
    private string OldDir => Path.Combine(_root, "MidiRouter");
    private string NewDir => Path.Combine(_root, "MidiMesh");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Copies_routes_and_log_and_leaves_the_old_files()
    {
        Directory.CreateDirectory(OldDir);
        File.WriteAllText(Path.Combine(OldDir, "routes.json"), "[\"old routes\"]");
        File.WriteAllText(Path.Combine(OldDir, "log.txt"), "old log");

        Assert.True(Migration.CopyOldData(OldDir, NewDir));

        Assert.Equal("[\"old routes\"]", File.ReadAllText(Path.Combine(NewDir, "routes.json")));
        Assert.Equal("old log", File.ReadAllText(Path.Combine(NewDir, "log.txt")));
        Assert.True(File.Exists(Path.Combine(OldDir, "routes.json")));
    }

    [Fact]
    public void Never_overwrites_routes_already_in_the_new_folder()
    {
        Directory.CreateDirectory(OldDir);
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(OldDir, "routes.json"), "old");
        File.WriteAllText(Path.Combine(NewDir, "routes.json"), "new");

        Assert.False(Migration.CopyOldData(OldDir, NewDir));
        Assert.Equal("new", File.ReadAllText(Path.Combine(NewDir, "routes.json")));
    }

    [Fact]
    public void Does_nothing_without_old_routes()
    {
        Assert.False(Migration.CopyOldData(OldDir, NewDir));
        Assert.False(Directory.Exists(NewDir));
    }

    [Fact]
    public void Moves_the_old_start_with_windows_value()
    {
        // A scratch key in place of the real Run key, so the test cannot change what starts at sign-in.
        var keyPath = @"Software\MidiMesh.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue(StartupRegistration.OldValueName, "\"C:\\old\\MidiRouter.exe\" --tray");
            }

            var command = StartupRegistration.CommandFor(@"C:\new\MidiMesh.exe");
            Assert.True(StartupRegistration.MoveOldValue(keyPath, "MidiMesh", command));

            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
            {
                Assert.Null(key.GetValue(StartupRegistration.OldValueName));
                Assert.Equal("\"C:\\new\\MidiMesh.exe\" --tray", key.GetValue("MidiMesh"));
            }

            // Second start: nothing left to move.
            Assert.False(StartupRegistration.MoveOldValue(keyPath, "MidiMesh", command));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MidiMesh.Tests", throwOnMissingSubKey: false);
        }
    }
}
