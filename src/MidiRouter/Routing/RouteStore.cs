using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidiRouter.Routing;

/// <summary>
/// One route from an input to an output, by device name. Names are kept rather than device ids
/// because Windows hands out a new id when a device is plugged into another port or re-enumerated.
/// The route's settings live in <see cref="RouteOptions"/>, so this stays a simple key.
/// </summary>
public sealed record Route(string From, string To);

/// <summary>One line of routes.json. Option fields are left out when they are at their default.</summary>
public sealed class RouteEntry
{
    public string From { get; set; } = "";

    public string To { get; set; } = "";

    /// <summary>Message kinds blocked on this route, for example "SysEx, Clock".</summary>
    public MessageKind? Block { get; set; }

    public int? FromChannel { get; set; }

    public int? ToChannel { get; set; }
}

public sealed class RouteFile
{
    public int Version { get; set; } = 1;

    public List<RouteEntry> Routes { get; set; } = [];
}

/// <summary>Loads and saves routes.json in the data folder.</summary>
public static class RouteStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<KeyValuePair<Route, RouteOptions>> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.RoutesFile)) return [];
            return Parse(File.ReadAllText(AppPaths.RoutesFile));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Info($"Could not read routes.json, starting with no routes: {ex.Message}");
            return [];
        }
    }

    /// <summary>Reads routes.json text. Blank names are skipped and channels outside 1 to 16 ignored.</summary>
    public static IReadOnlyList<KeyValuePair<Route, RouteOptions>> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<RouteFile>(json, Options);
        var result = new Dictionary<Route, RouteOptions>();
        foreach (var e in file?.Routes ?? [])
        {
            if (string.IsNullOrWhiteSpace(e.From) || string.IsNullOrWhiteSpace(e.To)) continue;
            result[new Route(e.From, e.To)] = new RouteOptions
            {
                Block = e.Block ?? MessageKind.None,
                FromChannel = ValidChannel(e.FromChannel),
                ToChannel = ValidChannel(e.ToChannel),
            };
        }
        return result.ToList();
    }

    public static string Serialize(IEnumerable<KeyValuePair<Route, RouteOptions>> routes)
    {
        var file = new RouteFile
        {
            Routes = routes
                .OrderBy(r => r.Key.From, StringComparer.Ordinal)
                .ThenBy(r => r.Key.To, StringComparer.Ordinal)
                .Select(r => new RouteEntry
                {
                    From = r.Key.From,
                    To = r.Key.To,
                    Block = r.Value.Block == MessageKind.None ? null : r.Value.Block,
                    FromChannel = r.Value.FromChannel,
                    ToChannel = r.Value.ToChannel,
                })
                .ToList(),
        };
        return JsonSerializer.Serialize(file, Options);
    }

    public static void Save(IEnumerable<KeyValuePair<Route, RouteOptions>> routes)
    {
        var temp = AppPaths.RoutesFile + ".tmp";
        try
        {
            File.WriteAllText(temp, Serialize(routes));
            File.Move(temp, AppPaths.RoutesFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Could not save routes.json: {ex.Message}");
        }
    }

    private static int? ValidChannel(int? channel) => channel is >= 1 and <= 16 ? channel : null;
}
