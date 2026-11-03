using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

internal sealed partial class Validation
{
    internal HashSet<string> MaterialFamilies { get; } = new(StringComparer.Ordinal);
    public List<ContentError> Warnings { get; } = [];
    private static readonly ImmutableSortedSet<string> DamageTypes = Ordinal.Set(
        "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder");

    public EnvironmentDef? Environment(JsonElement element, string file, string pointer) => Guarded(file, pointer, () =>
    {
        var valid = true;
        void Passives(JsonElement parent, string at)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty("passives", out var passives) || passives.ValueKind != JsonValueKind.Array)
                return;
            var index = 0;
            foreach (var passive in passives.EnumerateArray())
                valid &= Passive(passive, file, Pointer.Append(Pointer.Append(at, "passives"), index++)) is not null;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("surfaces", out var surfaces) && surfaces.ValueKind == JsonValueKind.Object)
                foreach (var surface in surfaces.EnumerateObject())
                    Passives(surface.Value, Pointer.Append(Pointer.Append(pointer, "surfaces"), surface.Name));
            if (element.TryGetProperty("reactions", out var reactions) && reactions.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var reaction in reactions.EnumerateArray())
                    Passives(reaction, Pointer.Append(Pointer.Append(pointer, "reactions"), index++));
            }
        }
        var def = Deserialize<EnvironmentDef>(element, file, pointer);
        if (def is null || !valid)
            return null;
        foreach (var (name, surface) in def.Surfaces)
        {
            var at = Pointer.Append(Pointer.Append(pointer, "surfaces"), name);
            if (surface.Rounds is <= 0)
                Error(file, Pointer.Append(at, "rounds"), "A surface lasts at least one round, or null for the battle.");
            if (surface.Leaves is { } leaves && !def.Surfaces.ContainsKey(leaves))
                Error(file, Pointer.Append(at, "leaves"), $"Unknown surface '{leaves}'.");
            if (surface.Spreads && (surface.Rounds is null || surface.Leaves is null))
                Error(file, at, "A spreading surface needs a finite duration and a permanent spent-fuel surface.");
            if (surface.Spreads && surface.Leaves is { } spent && def.Surfaces.TryGetValue(spent, out var remainder)
                && (remainder.Fuel || remainder.Spreads || remainder.Rounds is not null))
                Error(file, Pointer.Append(at, "leaves"), "Spent fuel must be permanent and cannot burn or spread again.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < def.Reactions.Length; i++)
        {
            var reaction = def.Reactions[i];
            var at = Pointer.Append(Pointer.Append(pointer, "reactions"), i);
            if (string.IsNullOrWhiteSpace(reaction.Id) || !ids.Add(reaction.Id))
                Error(file, Pointer.Append(at, "id"), "Reaction ids must be nonempty and unique.");
            if (reaction.Damage is { } type && !DamageTypes.Contains(type))
                Error(file, Pointer.Append(at, "damage"), $"Unknown damage type '{type}'.");
            if (reaction.Surface is { } kind && !def.Surfaces.ContainsKey(kind))
                Error(file, Pointer.Append(at, "surface"), $"Unknown surface '{kind}'.");
            if (reaction.Tiles is { } tiles && !(tiles is "water" or "shallows" or "fuel" || tiles.StartsWith("zone:", StringComparison.Ordinal)
                || tiles.StartsWith("surface:", StringComparison.Ordinal) && def.Surfaces.ContainsKey(tiles[8..])))
                Error(file, Pointer.Append(at, "tiles"), $"Unknown tile selector '{tiles}'.");
            if (reaction.Rounds is <= 0)
                Error(file, Pointer.Append(at, "rounds"), "A reaction's surface lasts at least one round.");
            if (reaction.Conduct is { Reach: < 1 or > MaxRange })
                Error(file, Pointer.Append(Pointer.Append(at, "conduct"), "reach"), $"Conduction reach must be in 1..{MaxRange}.");
            if (reaction.Remove is { } condition)
                Ref(RefKind.Condition, condition, file, Pointer.Append(at, "remove"));
        }
        return def;
    });

    public void CheckEnvironment(Loaded<EnvironmentDef>? loaded, IEnumerable<Loaded<MapContent>> maps)
    {
        if (loaded is null)
            return;
        var def = loaded.Def;
        var hasCatalogue = Art is not null || MaterialFamilies.Count > 0;
        // Map palettes define aliases too (for example mine_boards/oak uses the planks art).
        foreach (var map in maps)
            foreach (var material in map.Def.Map.Materials)
                MaterialFamilies.Add(material.Split('/')[0]);
        if (Art is { } art)
            foreach (var key in art.Keys.Where(k => k.StartsWith("terrain.", StringComparison.Ordinal)))
                MaterialFamilies.Add(key.Split('.')[1]);
        for (var i = 0; i < def.MaterialFamilies.Length; i++)
        {
            var family = def.MaterialFamilies[i];
            if (hasCatalogue && !MaterialFamilies.Contains(family))
                Error(loaded.File, Pointer.Append("/materialFamilies", i), $"Unknown material family '{family}'.");
        }
        // Rules-only copies keep an explicit family registry; supplied art is still authoritative.
        if (!hasCatalogue)
            MaterialFamilies.UnionWith(def.MaterialFamilies);
        for (var i = 0; i < def.Fuel.Length; i++)
        {
            var value = def.Fuel[i];
            var parts = value.Split('/');
            if (parts.Length != 2 || parts[1].Length == 0 || !MaterialFamilies.Contains(parts[0]) || parts[0].Contains('*'))
                Error(loaded.File, Pointer.Append("/fuel", i), $"Unknown material family or invalid fuel pattern '{value}'.");
        }
        foreach (var map in maps)
        {
            var ground = map.Def.Map;
            var remaining = new SortedSet<Tile>();
            for (var y = 0; y < ground.Height; y++)
                for (var x = 0; x < ground.Width; x++)
                {
                    var tile = new Tile(x, y);
                    var material = ground.Materials[ground[tile].Material];
                    if (def.Fuel.Any(f => MatchesMaterial(material, f)))
                        remaining.Add(tile);
                }
            while (remaining.Count > 0)
            {
                var pending = new Queue<Tile>();
                var first = remaining.Min;
                remaining.Remove(first);
                pending.Enqueue(first);
                var count = 0;
                while (pending.TryDequeue(out var tile))
                {
                    count++;
                    foreach (var next in new[] { new Tile(tile.X - 1, tile.Y), new Tile(tile.X, tile.Y - 1), new Tile(tile.X, tile.Y + 1), new Tile(tile.X + 1, tile.Y) })
                        if (remaining.Remove(next))
                            pending.Enqueue(next);
                }
                if (count > 12)
                    Warnings.Add(new ContentError(map.File, "/tiles", $"Connected fuel patch at {first} has {count} tiles (over 12)."));
            }
        }
    }

    internal static bool MatchesMaterial(string material, string pattern)
        => pattern.EndsWith("/*", StringComparison.Ordinal) ? material.StartsWith(pattern[..^1], StringComparison.Ordinal) : material == pattern;
}
