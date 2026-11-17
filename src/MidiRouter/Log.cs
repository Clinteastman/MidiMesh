namespace MidiRouter;

/// <summary>Small file log. Plug and unplug events go here so a dropped device can be explained later.</summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static event Action<string>? Written;

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        lock (Gate)
        {
            try
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(file.FullName, file.FullName + ".old", overwrite: true);
                }
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never stop the routing.
            }
        }
        Written?.Invoke(line);
    }
}
