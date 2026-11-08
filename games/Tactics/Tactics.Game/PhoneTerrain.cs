using System.Collections.Immutable;
using System.Globalization;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A live spell/surface zone, described without predicting damage or a unit's next move.</summary>
public sealed record PhoneTerrainZone(string Name, ImmutableArray<Tile> Tiles, bool Hazard, string Detail);

/// <summary>Compact row-major terrain matching the engine, including changed heights, sunk tiles and blocking props.</summary>
public sealed record PhoneTerrain(int W, int H, ImmutableArray<int> Heights, ImmutableArray<int> Flags,
    ImmutableArray<string> Materials, ImmutableArray<int> TileMaterial, ImmutableArray<PhoneTerrainZone> Zones)
{
    public static PhoneTerrain Of(BattleState state, RulesPack rules)
    {
        var terrain = new BattleWriter(state, rules).Terrain;
        var map = terrain.Map;
        var tiles = Enumerable.Range(0, map.Width * map.Height).Select(i => new Tile(i % map.Width, i / map.Width)).ToArray();
        int FlagsAt(Tile tile)
        {
            var flags = map[tile].Flags;
            if (!terrain.Walkable(tile)) flags |= TileFlags.Impassable;
            if (terrain.Difficult(tile)) flags |= TileFlags.Difficult;
            if (terrain.Deep(tile)) flags |= TileFlags.Deep;
            return (int)flags;
        }
        PhoneTerrainZone Zone(Zone zone)
        {
            var passives = zone.Passives.IsDefault ? [] : zone.Passives;
            var hazard = zone.Difficult || zone.BlocksSight || zone.Kind is "burning" or "fire" or "ice_sheet" or "oil" or "grease"
                || EffectTree.All([.. passives.SelectMany(p => p.Effects.IsDefault ? [] : p.Effects)]).Any(e => e is DamageEffect);
            var detail = new List<string>();
            if (zone.Difficult) detail.Add("Difficult ground: entering costs 2 Move.");
            if (zone.BlocksSight) detail.Add("Blocks line of sight.");
            if (zone.Affects is { } affects && affects != AffectedSide.All)
            {
                var owner = zone.Owner is { } id ? state.Units.GetValueOrDefault(id)?.Sheet.Name : null;
                detail.Add($"Affects {(affects == AffectedSide.Allies ? "allies" : "enemies")} of {owner ?? "the owner"}.");
            }
            var level = zone.Owner is { } source ? state.Units.GetValueOrDefault(source)?.Sheet.Level ?? 1 : 1;
            detail.AddRange(passives.Select(p => Tooltips.For(p, level, rules)));
            var name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(zone.Kind.Replace('_', ' ').Replace('.', ' '));
            return new(name, zone.Tiles, hazard, string.Join(" ", detail));
        }
        return new(map.Width, map.Height, [.. tiles.Select(terrain.HeightAt)], [.. tiles.Select(FlagsAt)],
            map.Materials, [.. tiles.Select(t => map[t].Material)], [.. state.Zones.Select(Zone)]);
    }
}
