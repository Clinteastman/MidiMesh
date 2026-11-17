namespace MidiMesh;

/// <summary>One-time move from the old "MIDI Router" name: copies the routes and log across.</summary>
public static class Migration
{
    private static readonly string[] Files = ["routes.json", "log.txt", "log.txt.old"];

    /// <summary>
    /// Copies routes.json and the log from <paramref name="oldDir"/> into <paramref name="newDir"/>
    /// when the new folder has no routes yet. The old files are left in place, so an old copy that
    /// is still running keeps working. Returns true when routes were copied.
    /// </summary>
    public static bool CopyOldData(string oldDir, string newDir)
    {
        var oldRoutes = Path.Combine(oldDir, "routes.json");
        if (!File.Exists(oldRoutes) || File.Exists(Path.Combine(newDir, "routes.json"))) return false;

        Directory.CreateDirectory(newDir);
        foreach (var name in Files)
        {
            var from = Path.Combine(oldDir, name);
            var to = Path.Combine(newDir, name);
            if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
        }
        return true;
    }
}
