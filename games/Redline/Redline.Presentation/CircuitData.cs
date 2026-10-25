using System.Numerics;
using System.Text.Json;

namespace Redline.Presentation;

/// <summary>
/// A closed circuit centre line plus an elevation profile. Points are metres, x = east, y = south
/// (the race direction is increasing index); elevation keyframes are (metres along the lap, height metres).
/// </summary>
public sealed record CircuitData(string Name, IReadOnlyList<Vector2> Points, IReadOnlyList<(double S, double H)> Elevation)
{
    private const string ResourcePrefix = "Redline.Presentation.Data.";

    private static readonly Dictionary<string, Lazy<CircuitData>> Cache = new(StringComparer.Ordinal)
    {
        ["cota"] = new(() => LoadResource("cota")),
    };

    /// <summary>Length of the closed polyline through <see cref="Points"/>, in metres.</summary>
    public double LapLength { get; } = ClosedLength(Points);

    /// <summary>The embedded circuit with this id ("cota"). Parsed and validated once, then cached.</summary>
    public static CircuitData Load(string id)
    {
        if (id is null || !Cache.TryGetValue(id, out var lazy))
            throw new KeyNotFoundException($"Unknown circuit '{id}'.");
        return lazy.Value;
    }

    private static double ClosedLength(IReadOnlyList<Vector2> points)
    {
        double total = 0;
        for (var i = 0; i < points.Count; i++)
            total += Vector2.Distance(points[i], points[(i + 1) % points.Count]);
        return total;
    }

    private static CircuitData LoadResource(string id)
    {
        var resource = $"{ResourcePrefix}{id}.json";
        using var stream = typeof(CircuitData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Embedded resource '{resource}' not found.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{id}.json has no name.");

        var points = ReadPairs(root, "points", id).Select(p => new Vector2((float)p.A, (float)p.B)).ToArray();
        if (points.Length < 3)
            throw new InvalidDataException($"{id}.json needs at least 3 points, found {points.Length}.");

        var elevation = ReadPairs(root, "elevation", id).Select(p => (S: p.A, H: p.B)).ToArray();
        if (elevation.Length < 2)
            throw new InvalidDataException($"{id}.json needs at least 2 elevation keyframes, found {elevation.Length}.");
        for (var i = 1; i < elevation.Length; i++)
            if (elevation[i].S <= elevation[i - 1].S)
                throw new InvalidDataException($"{id}.json elevation keyframes must have strictly increasing s (index {i}).");

        return new CircuitData(name, points, elevation);
    }

    private static List<(double A, double B)> ReadPairs(JsonElement root, string property, string id)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{id}.json has no '{property}' array.");
        var result = new List<(double, double)>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2
                || item[0].ValueKind != JsonValueKind.Number || item[1].ValueKind != JsonValueKind.Number)
                throw new InvalidDataException($"{id}.json '{property}' entries must be [number, number].");
            result.Add((item[0].GetDouble(), item[1].GetDouble()));
        }
        return result;
    }
}
