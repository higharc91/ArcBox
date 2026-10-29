using System.Numerics;
using System.Text.Json;
using Redline.Presentation.Course;

namespace Redline.Presentation;

/// <summary>
/// A closed circuit centre line plus an elevation profile. Points are metres, x = east, y = south
/// (the race direction is increasing index); elevation keyframes are (metres along the lap, height metres).
/// <see cref="Corners"/> are the official turns in lap order and <see cref="StraightModeZones"/> the 2026 active-aero zones
/// (both optional in the json, empty when absent).
/// </summary>
public sealed record CircuitData(string Name, IReadOnlyList<Vector2> Points, IReadOnlyList<(double S, double H)> Elevation)
{
    private const string ResourcePrefix = "Redline.Presentation.Data.";

    private static readonly Dictionary<string, Lazy<CircuitData>> Cache = new(StringComparer.Ordinal)
    {
        ["cota"] = new(() => LoadResource("cota")),
    };

    /// <summary>The official turns, in lap order (strictly increasing apex s).</summary>
    public IReadOnlyList<Corner> Corners { get; init; } = [];

    /// <summary>The straight-mode (active aero) zones.</summary>
    public IReadOnlyList<StraightModeZone> StraightModeZones { get; init; } = [];

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

        return new CircuitData(name, points, elevation)
        {
            Corners = ReadCorners(root, id),
            StraightModeZones = ReadZones(root, id),
        };
    }

    private static Corner[] ReadCorners(JsonElement root, string id)
    {
        if (!root.TryGetProperty("corners", out var array))
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{id}.json 'corners' must be an array.");
        var result = new List<Corner>();
        foreach (var item in array.EnumerateArray())
        {
            string Text(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!
                : throw new InvalidDataException($"{id}.json corner {result.Count + 1} has no '{key}'.");
            double Number(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble()
                : throw new InvalidDataException($"{id}.json corner {result.Count + 1} has no '{key}'.");
            int Side(string key) => Text(key) switch
            {
                "left" => -1,
                "right" => +1,
                var other => throw new InvalidDataException($"{id}.json corner '{key}' must be left or right, not '{other}'."),
            };
            T Pick<T>(string key, params (string Name, T Value)[] options)
            {
                var text = Text(key);
                foreach (var (optionName, value) in options)
                    if (optionName == text) return value;
                throw new InvalidDataException($"{id}.json corner '{key}' has unknown value '{text}'.");
            }

            var direction = Side("direction");
            result.Add(new Corner(
                (int)Number("turn"),
                Number("s"),
                direction,
                // apexSide is stored as a direction word; as a lateral sign, left of travel is +1.
                -Side("apexSide"),
                Pick("kerb", ("flat", KerbKind.Flat), ("sawtooth", KerbKind.Sawtooth), ("sausage", KerbKind.Sausage)),
                Pick("runoff", ("tarmac-striped", RunoffKind.TarmacStriped), ("tarmac", RunoffKind.Tarmac), ("gravel", RunoffKind.Gravel), ("grass", RunoffKind.Grass)),
                Pick("barrier", ("tecpro", BarrierKind.TecPro), ("tyres", BarrierKind.Tyres), ("armco", BarrierKind.Armco), ("wall", BarrierKind.Wall)),
                Pick("braking", ("none", Braking.None), ("light", Braking.Light), ("medium", Braking.Medium), ("heavy", Braking.Heavy)),
                Pick("confidence", ("low", Confidence.Low), ("medium", Confidence.Medium), ("high", Confidence.High))));
        }
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].Turn != i + 1)
                throw new InvalidDataException($"{id}.json corners must be numbered 1, 2, … in order (entry {i + 1} is turn {result[i].Turn}).");
            if (i > 0 && result[i].S <= result[i - 1].S)
                throw new InvalidDataException($"{id}.json corner apexes must have strictly increasing s (turn {i + 1}).");
        }
        return [.. result];
    }

    private static StraightModeZone[] ReadZones(JsonElement root, string id)
    {
        if (!root.TryGetProperty("straightMode", out var array))
            return [];
        if (array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{id}.json 'straightMode' must be an array.");
        var result = new List<StraightModeZone>();
        foreach (var item in array.EnumerateArray())
        {
            if (!item.TryGetProperty("activation", out var a) || a.ValueKind != JsonValueKind.Number
                || !item.TryGetProperty("end", out var e) || e.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException($"{id}.json straight-mode zones need numeric 'activation' and 'end'.");
            result.Add(new StraightModeZone(a.GetDouble(), e.GetDouble()));
        }
        return [.. result];
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
