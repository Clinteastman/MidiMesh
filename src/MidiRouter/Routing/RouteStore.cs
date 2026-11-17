using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidiRouter.Routing;

/// <summary>
/// One route from an input to an output, by device name. Names are kept rather than device ids
/// because Windows hands out a new id when a device is plugged into another port or re-enumerated.
/// Later per-route options (channel or CC remapping) belong on this record.
/// </summary>
public sealed record Route(string From, string To);

public sealed class RouteFile
{
    public int Version { get; set; } = 1;

    public List<Route> Routes { get; set; } = [];
}

/// <summary>Loads and saves routes.json in the data folder.</summary>
public static class RouteStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyList<Route> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.RoutesFile)) return [];
            var file = JsonSerializer.Deserialize<RouteFile>(File.ReadAllText(AppPaths.RoutesFile), Options);
            return file?.Routes
                .Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To))
                .Distinct()
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Info($"Could not read routes.json, starting with no routes: {ex.Message}");
            return [];
        }
    }

    public static void Save(IEnumerable<Route> routes)
    {
        var file = new RouteFile { Routes = routes.OrderBy(r => r.From).ThenBy(r => r.To).ToList() };
        var temp = AppPaths.RoutesFile + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
            File.Move(temp, AppPaths.RoutesFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save routes.json: {ex.Message}");
        }
    }
}
