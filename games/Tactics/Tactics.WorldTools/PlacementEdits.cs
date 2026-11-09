using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tactics.Logic;

namespace Tactics.WorldTools;

public enum PlacementKind { Interactable, MobPatch }
public sealed record PlacementPoint(PlacementKind Kind, string Id, Tile At, ImmutableArray<Tile> Offsets = default);
public sealed record ShopBinding(string Id, int Person);

/// <summary>Coordinate-only editing of authored side content. No-op plans preserve every original byte.</summary>
public sealed class PlacementEdits
{
    private readonly Dictionary<string, JsonObject> _shops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<(PlacementKind, string), (string File, JsonObject Entry)> _entries = [];
    public ImmutableArray<PlacementPoint> Points { get; private set; } = [];
    public ImmutableArray<ShopBinding> Shops { get; private set; } = [];

    public static PlacementEdits Read(string rules, string region)
    {
        var result = new PlacementEdits();
        var points = ImmutableArray.CreateBuilder<PlacementPoint>();
        var shops = ImmutableArray.CreateBuilder<ShopBinding>();
        var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(rules, "manifest.json")))!.AsObject();
        foreach (var entry in manifest["files"]!.AsArray().Select(f => f!.GetValue<string>()))
        {
            if (!entry.StartsWith("interactables/", StringComparison.Ordinal) && !entry.StartsWith("mobs/", StringComparison.Ordinal)
                && !entry.StartsWith("shops/", StringComparison.Ordinal)) continue;
            if (Path.IsPathRooted(entry) || entry.Split('/', '\\').Contains("..")) throw new InvalidDataException("Placement file must be inside its rules pack.");
            var doc = JsonNode.Parse(File.ReadAllBytes(Path.Combine(rules, entry)))!.AsObject();
            if (doc["region"]?.GetValue<string>() != region) continue;
            if (doc["shops"] is JsonArray sale)
            {
                result._shops.Add(entry, doc);
                foreach (var shop in sale.OfType<JsonObject>())
                    if (shop["keeper"]?["person"] is JsonValue person && person.TryGetValue<int>(out var index))
                        shops.Add(new(shop["id"]!.GetValue<string>(), index));
                continue;
            }
            result._documents.Add(entry, doc);
            var kind = doc["interactables"] is not null ? PlacementKind.Interactable : PlacementKind.MobPatch;
            foreach (var node in doc[kind == PlacementKind.Interactable ? "interactables" : "mobs"]!.AsArray().OfType<JsonObject>())
            {
                var id = node["id"]!.GetValue<string>();
                result._entries.Add((kind, id), (entry, node));
                var tiles = kind == PlacementKind.Interactable ? new[] { ReadTile(node["at"]!) }
                    : node["patch"]!.AsArray().Select(t => ReadTile(t!)).ToArray();
                if (tiles.Length == 0) throw new InvalidDataException($"Mob patch '{id}' is empty.");
                var at = tiles[0];
                points.Add(new(kind, id, at, kind == PlacementKind.MobPatch ? [.. tiles.Select(t => new Tile(t.X - at.X, t.Y - at.Y))] : default));
            }
        }
        result.Points = points.ToImmutable(); result.Shops = shops.ToImmutable();
        return result;
    }

    public IReadOnlyDictionary<string, byte[]> Plan(IEnumerable<PlacementPoint> markers)
    {
        var list = markers.ToArray();
        if (list.Select(p => (p.Kind, p.Id)).Distinct().Count() != list.Length)
            throw new InvalidDataException("Placement markers must have unique kind/id pairs.");
        if (!_entries.Keys.ToHashSet().SetEquals(list.Select(p => (p.Kind, p.Id))))
            throw new InvalidDataException("Placement markers do not match the authored objects. Import placements again; unknown or missing markers cannot delete content.");
        var copies = _documents.ToDictionary(p => p.Key, p => p.Value.DeepClone().AsObject(), StringComparer.Ordinal);
        foreach (var point in list)
        {
            var (file, _) = _entries[(point.Kind, point.Id)];
            var key = point.Kind == PlacementKind.Interactable ? "interactables" : "mobs";
            var node = copies[file][key]!.AsArray().OfType<JsonObject>().Single(n => n["id"]!.GetValue<string>() == point.Id);
            if (point.Kind == PlacementKind.Interactable) node["at"] = TileNode(point.At);
            else
            {
                if (point.Offsets.IsDefaultOrEmpty || point.Offsets.Distinct().Count() != point.Offsets.Length)
                    throw new InvalidDataException($"Patch '{point.Id}' needs distinct offset tiles.");
                node["patch"] = new JsonArray(point.Offsets.Select(t => (JsonNode)TileNode(new Tile(point.At.X + t.X, point.At.Y + t.Y))).ToArray());
            }
        }
        return copies.Where(p => !JsonNode.DeepEquals(_documents[p.Key], p.Value)).ToDictionary(p => p.Key,
            p => Encoding.UTF8.GetBytes(p.Value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, byte[]> PlanShops(IReadOnlyDictionary<string, int> people)
    {
        if (!Shops.Select(s => s.Id).ToHashSet(StringComparer.Ordinal).SetEquals(people.Keys))
            throw new InvalidDataException("Shop labels must match the authored shops; retain each keeper's ShopId.");
        var updates = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (file, original) in _shops)
        {
            var copy = original.DeepClone().AsObject();
            foreach (var shop in copy["shops"]!.AsArray().OfType<JsonObject>())
                if (shop["keeper"]?["person"] is not null)
                    shop["keeper"]!["person"] = people[shop["id"]!.GetValue<string>()];
            if (!JsonNode.DeepEquals(original, copy))
                updates.Add(file, Encoding.UTF8.GetBytes(copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"));
        }
        return updates;
    }

    private static Tile ReadTile(JsonNode value) => new(value[0]!.GetValue<int>(), value[1]!.GetValue<int>());
    private static JsonArray TileNode(Tile tile) => new(tile.X, tile.Y);
}
