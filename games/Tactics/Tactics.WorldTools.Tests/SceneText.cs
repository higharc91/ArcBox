using System.Globalization;
using System.Text.RegularExpressions;
using Tactics.Logic;

namespace Tactics.WorldTools.Tests;

/// <summary>
/// Reads a saved region scene from its text, so the tests can re-export it with no Godot on the machine. It reads what
/// <c>RegionScene.Read</c> reads from the live nodes, and throws on a tile layer in any other format than the one it knows, so a Godot
/// upgrade that changes the file fails here instead of reading nonsense.
/// </summary>
internal static partial class SceneText
{
    /// <summary>The bytes of one cell in a <c>tile_map_data</c>: x, y, source, atlas x, atlas y and alternative, two bytes each.</summary>
    private const int CellBytes = 12;

    private sealed record Node(string Name, string? Parent, Dictionary<string, string> Values)
    {
        public string? this[string key] => Values.GetValueOrDefault(key);

        public Tile Tile
        {
            get
            {
                var (x, y) = Vector(this["position"]!);
                return PaintedRegion.TileAt(x, y);
            }
        }
    }

    private readonly record struct Cell(Tile At, int Source, int AtlasX, int AtlasY);

    [GeneratedRegex("""(\w+)="([^"]*)"|(\w+)=(\d+)""", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"\((-?[\d.]+), (-?[\d.]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();

    [GeneratedRegex("""^sources/(\d+) = SubResource\("([^"]+)"\)$""", RegexOptions.CultureInvariant)]
    private static partial Regex Source();

    /// <summary>The scene at <paramref name="scene"/>, naming its materials from the tile set at <paramref name="materials"/>.</summary>
    public static PaintedRegion Read(string scene, string materials)
    {
        var nodes = Nodes(scene);
        var root = nodes.Single(n => n.Parent is null);
        var (width, height) = Vector(root["Size"] ?? "(32, 32)");
        var names = MaterialNames(materials);
        var spawns = nodes.Where(n => n.Parent == "Spawns").ToList();

        return new PaintedRegion(Text(root["Id"]), Text(root["Name"]), int.Parse(root["Chapter"] ?? "1", CultureInfo.InvariantCulture), (int)width, (int)height)
        {
            Materials = Cells(nodes, "Material").ToDictionary(c => c.At, c => names[c.Source]),
            Heights = Cells(nodes, "Height").ToDictionary(c => c.At, c => c.AtlasX),
            Flags = Cells(nodes, "Flags").ToDictionary(c => c.At, c => PaletteBuilder.FlagsAt(c.AtlasX, c.AtlasY)),
            Beats = [.. nodes.Where(n => n.Parent == "Beats").Select(n => new BeatSpot(Text(n["Beat"]), n.Tile))],
            Stamps = [.. nodes.Where(n => n.Parent == "Stamps").Select(n => new RegionStamp(Text(n["Map"]), n.Tile, int.Parse(n["HeightOffset"] ?? "0", CultureInfo.InvariantCulture)))],
            Starts = [.. spawns.Where(n => (n["Kind"] ?? "0") == "0").Select(n => n.Tile)],
            Recoveries = [.. spawns.Where(n => n["Kind"] == "1").Select(n => n.Tile)],
            SceneFile = Path.GetFileNameWithoutExtension(scene),
        };
    }

    private static string Text(string? value) => value?.Trim('"') ?? "";

    private static (double X, double Y) Vector(string value)
    {
        var pair = Pair().Match(value);
        return (double.Parse(pair.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static List<Node> Nodes(string path)
    {
        var nodes = new List<Node>();
        Node? current = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("[node ", StringComparison.Ordinal))
            {
                var attributes = Attribute().Matches(line).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                nodes.Add(current = new Node(attributes["name"], attributes.GetValueOrDefault("parent"), []));
            }
            else if (line.StartsWith('['))
            {
                current = null;
            }
            else if (current is not null && line.IndexOf(" = ", StringComparison.Ordinal) is > 0 and var equals)
            {
                current.Values[line[..equals]] = line[(equals + 3)..];
            }
        }
        return nodes;
    }

    private static Dictionary<int, string> MaterialNames(string path)
    {
        var (named, sources, id) = (new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<int, string>(), "");
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("[sub_resource ", StringComparison.Ordinal))
                id = Attribute().Matches(line).Single(m => m.Groups[1].Value == "id").Groups[2].Value;
            else if (line.StartsWith("resource_name = ", StringComparison.Ordinal))
                named[id] = Text(line["resource_name = ".Length..]);
            else if (Source().Match(line) is { Success: true } source)
                sources[int.Parse(source.Groups[1].Value, CultureInfo.InvariantCulture)] = named[source.Groups[2].Value];
        }
        return sources;
    }

    private static List<Cell> Cells(List<Node> nodes, string layer)
    {
        if (nodes.Single(n => n.Name == layer)["tile_map_data"] is not { } data)
            return [];

        const string Open = "PackedByteArray(\"";
        var bytes = Convert.FromBase64String(data[Open.Length..^2]);
        if (bytes[0] != 0 || bytes[1] != 0 || (bytes.Length - 2) % CellBytes != 0)
            throw new InvalidDataException($"The {layer} layer isn't in the tile format this reader knows.");

        short At(int offset) => BitConverter.ToInt16(bytes, offset);
        var cells = new List<Cell>();
        for (var i = 2; i < bytes.Length; i += CellBytes)
            cells.Add(new Cell(new Tile(At(i), At(i + 2)), At(i + 4), At(i + 6), At(i + 8)));
        return cells;
    }
}
