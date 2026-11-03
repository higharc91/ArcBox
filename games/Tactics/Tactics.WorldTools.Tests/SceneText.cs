using System.Globalization;
using System.Text.RegularExpressions;
using Tactics.Logic;

namespace Tactics.WorldTools.Tests;

/// <summary>
/// Reads a saved region scene from its text, so the tests can re-export it with no Godot on the machine. It reads all that
/// <c>RegionScene.Read</c> reads from the live nodes, old stamp areas, strays and moved nodes included, so the export judges it the same
/// way. It throws on a tile layer in any other format than the one it knows, so a Godot upgrade that changes the file fails here instead
/// of reading nonsense.
/// </summary>
internal static partial class SceneText
{
    /// <summary>The bytes of one cell in a <c>tile_map_data</c>: x, y, source, atlas x, atlas y and alternative, two bytes each.</summary>
    private const int CellBytes = 12;

    private static readonly string[] Layers = ["Material", "Height", "Flags", "Props"];

    private static readonly Dictionary<string, string> MarkerGroups = new(StringComparer.Ordinal)
    {
        ["BeatMarker"] = "Beats",
        ["StampMarker"] = "Stamps",
        ["SpawnMarker"] = "Spawns",
        ["BuildingMarker"] = "Buildings",
        ["PersonMarker"] = "People",
    };

    private sealed record Node(string Name, string? Parent, Dictionary<string, string> Values)
    {
        /// <summary>The marker class of the node's script, or null if it isn't a marker.</summary>
        public string? Marker { get; init; }

        public string? this[string key] => Values.GetValueOrDefault(key);

        /// <summary>A position at the origin isn't saved, so a stamp at tile (0, 0) has no such line.</summary>
        public Tile Tile
        {
            get
            {
                var (x, y) = Vector(this["position"] ?? "(0, 0)");
                return PaintedRegion.TileAt(x, y);
            }
        }

        public bool Moved => (this["position"] is { } position && Vector(position) != (0, 0))
            || this["rotation"] is not null || this["scale"] is not null || this["skew"] is not null;

        public string ParentName => Parent == "." ? "the root" : Parent![(Parent!.LastIndexOf('/') + 1)..];
    }

    private readonly record struct Cell(Tile At, int Source, int AtlasX, int AtlasY);

    /// <summary>A <c>name="text"</c> or <c>name=1</c> in a section header. Text can hold escaped quotes.</summary>
    [GeneratedRegex("""(\w+)=(?:"((?:[^"\\]|\\.)*)"|([^\s\]]+))""", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"\((-?[\d.]+), (-?[\d.]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex Pair();

    [GeneratedRegex("""^sources/(\d+) = SubResource\("([^"]+)"\)$""", RegexOptions.CultureInvariant)]
    private static partial Regex Source();

    [GeneratedRegex("""
        "((?:[^"\\]|\\.)*)"
        """, RegexOptions.CultureInvariant)]
    private static partial Regex Quoted();

    /// <summary>The scene at <paramref name="scene"/>, naming its materials from the tile set at <paramref name="materials"/>.</summary>
    public static PaintedRegion Read(string scene, string materials)
    {
        var nodes = Nodes(scene);
        var root = nodes.Single(n => n.Parent is null);
        var (width, height) = Vector(root["Size"] ?? "(32, 32)");
        var names = MaterialNames(materials);
        var spawns = Markers(nodes, "SpawnMarker");
        var stamps = Markers(nodes, "StampMarker");

        return new PaintedRegion(Text(root["Id"]), Text(root["Name"]), int.Parse(root["Chapter"] ?? "1", CultureInfo.InvariantCulture), (int)width, (int)height)
        {
            // A source the tile set no longer has keeps a name, as RegionScene does, so the export can name the tile.
            Materials = Cells(nodes, "Material").ToDictionary(c => c.At, c => names.GetValueOrDefault(c.Source, $"source {c.Source}")),
            Heights = Cells(nodes, "Height").ToDictionary(c => c.At, c => c.AtlasX),
            Flags = Cells(nodes, "Flags").ToDictionary(c => c.At, c => PaletteBuilder.FlagsAt(c.AtlasX, c.AtlasY)),
            Props = Cells(nodes, "Props").ToDictionary(c => c.At, c => PaletteBuilder.PropAt(c.AtlasX, c.AtlasY)),
            Beats = [.. Markers(nodes, "BeatMarker").Select(n => new BeatSpot(Text(n["Beat"]), n.Tile))],
            Buildings = [.. Markers(nodes, "BuildingMarker").Select(n => new RegionBuilding(Text(n["Building"]), Text(n["Variant"]), n.Tile))],
            People = [.. Markers(nodes, "PersonMarker").Select(n => new RegionPerson(Text(n["Preset"]), n.Tile, n["Face"] is null ? "sw" : Text(n["Face"]), PersonText.Walk(Text(n["Walk"]))))],
            Stamps = [.. stamps.Select(n => new RegionStamp(Text(n["Map"]), n.Tile, int.Parse(n["HeightOffset"] ?? "0", CultureInfo.InvariantCulture)))],
            Starts = [.. spawns.Where(n => (n["Kind"] ?? "0") == "0").Select(n => n.Tile)],
            Recoveries = [.. spawns.Where(n => n["Kind"] == "1").Select(n => n.Tile)],
            OldStamps = [.. stamps.SelectMany(OldAreas)],
            MovedNodes = [.. Layers.Concat(MarkerGroups.Values.Order(StringComparer.Ordinal)).Where(name => nodes.SingleOrDefault(n => n.Parent == "." && n.Name == name)?.Moved == true)],
            Strays = Strays(nodes),
            SceneFile = Path.GetFileNameWithoutExtension(scene),
        };
    }

    private static List<Node> Markers(List<Node> nodes, string marker) => [.. nodes.Where(n => n.Marker == marker && n.Parent == MarkerGroups[marker])];

    private static IEnumerable<RegionStamp> OldAreas(Node stamp)
        => stamp["PendingAreas"] is { } areas ? Quoted().Matches(areas).Select(m => StampBrush.Decode(Unescape(m.Groups[1].Value))).OfType<RegionStamp>() : [];

    private static List<StrayNode> Strays(List<Node> nodes)
    {
        var strays = new List<StrayNode>();
        foreach (var node in nodes.Where(n => n.Marker is not null && n.Parent != MarkerGroups[n.Marker]))
            strays.Add(new StrayNode($"{node.Marker} '{node.Name}'", node.ParentName, $"Move it under {MarkerGroups[node.Marker!]}", node.Tile));
        foreach (var node in nodes.Where(n => n.Marker is null && n.Parent is not null && MarkerGroups.ContainsValue(n.Parent)))
            strays.Add(new StrayNode($"node '{node.Name}'", node.Parent!, "Give it a marker type, or delete it", node.Tile));
        return strays;
    }

    private static string Text(string? value) => value is null ? "" : Unescape(value.Trim().Length >= 2 ? value.Trim()[1..^1] : value);

    private static string Unescape(string text) => Regex.Replace(text, @"\\(.)", m => m.Groups[1].Value switch
    {
        "n" => "\n",
        "t" => "\t",
        var other => other,
    });

    private static (double X, double Y) Vector(string value)
    {
        var pair = Pair().Match(value);
        return (double.Parse(pair.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, string> Attributes(string header)
        => Attribute().Matches(header).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Success ? Unescape(m.Groups[2].Value) : m.Groups[3].Value);

    private static List<Node> Nodes(string path)
    {
        var (nodes, scripts) = (new List<Node>(), new Dictionary<string, string>(StringComparer.Ordinal));
        Node? current = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("[ext_resource ", StringComparison.Ordinal))
            {
                var attributes = Attributes(line);
                scripts[attributes["id"]] = Path.GetFileNameWithoutExtension(attributes["path"]);
            }
            else if (line.StartsWith("[node ", StringComparison.Ordinal))
            {
                var attributes = Attributes(line);
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
        return [.. nodes.Select(n => n with { Marker = MarkerOf(n, scripts) })];
    }

    private static string? MarkerOf(Node node, Dictionary<string, string> scripts)
    {
        if (node["script"] is not { } script || !scripts.TryGetValue(Quoted().Match(script).Groups[1].Value, out var name))
            return null;
        return MarkerGroups.ContainsKey(name) ? name : null;
    }

    private static Dictionary<int, string> MaterialNames(string path)
    {
        var (named, sources, id) = (new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<int, string>(), "");
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("[sub_resource ", StringComparison.Ordinal))
                id = Attributes(line)["id"];
            else if (line.StartsWith("resource_name = ", StringComparison.Ordinal))
                named[id] = Text(line["resource_name = ".Length..]);
            else if (Source().Match(line) is { Success: true } source)
                sources[int.Parse(source.Groups[1].Value, CultureInfo.InvariantCulture)] = named[source.Groups[2].Value];
        }
        return sources;
    }

    private static List<Cell> Cells(List<Node> nodes, string layer)
    {
        if (nodes.SingleOrDefault(n => n.Parent == "." && n.Name == layer)?["tile_map_data"] is not { } data)
            return [];

        const string Open = "PackedByteArray(\"";
        var bytes = Convert.FromBase64String(data[Open.Length..^2]);
        if (bytes[0] != 0 || bytes[1] != 0 || (bytes.Length - 2) % CellBytes != 0)
            throw new InvalidDataException($"The {layer} layer isn't in the tile format this reader knows.");

        short At(int offset) => BitConverter.ToInt16(bytes, offset);
        var cells = new List<Cell>();
        for (var i = 2; i < bytes.Length; i += CellBytes)
            if (At(i + 4) >= 0)     // a cell with source -1 is an erased one, which Godot can keep in the data: GetUsedCells leaves it out
                cells.Add(new Cell(new Tile(At(i), At(i + 2)), At(i + 4), At(i + 6), At(i + 8)));
        return cells;
    }
}
