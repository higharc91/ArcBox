using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The wall the TV draws over a gap where a secret door nobody has found stands (<see cref="ExploreView.Plugs"/>). The region is painted with
/// the gap open, as a passage is open ground while open; drawn as it's painted, the gap would give the door away. So each plug is drawn as its
/// wall's own tile: the height, material and flags of the taller of its neighbours along the wall's run, built and lit as the world's blocks
/// are, so it can't be told from the wall beside it. Pure, and built off the main thread: plugs change only when a door is found.
/// </summary>
public static class WallPlugs
{
    /// <summary>The tile a plug is drawn as: its wall's, from the taller of its two neighbours along the run, or its own if neither is there.</summary>
    public static TileInfo WallFor(BattleMap map, WallPlug plug)
    {
        var (a, b) = plug.Facing is "se" or "nw"
            ? (new Tile(plug.At.X, plug.At.Y - 1), new Tile(plug.At.X, plug.At.Y + 1))
            : (new Tile(plug.At.X - 1, plug.At.Y), new Tile(plug.At.X + 1, plug.At.Y));
        TileInfo? best = null;
        foreach (var side in new[] { a, b })
            if (map.InBounds(side) && (best is null || map[side].Height > best.Height))
                best = map[side];
        return best ?? map[plug.At];
    }

    /// <summary><paramref name="map"/> with each plug's tile its wall's (<see cref="WallFor"/>), and everything else as it was.</summary>
    public static BattleMap With(BattleMap map, IReadOnlyCollection<WallPlug> plugs)
    {
        var bytes = new byte[map.Width * map.Height * 3];
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var tile = map[new Tile(x, y)];
                (bytes[(y * map.Width + x) * 3], bytes[(y * map.Width + x) * 3 + 1], bytes[(y * map.Width + x) * 3 + 2]) =
                    ((byte)tile.Height, (byte)tile.Material, (byte)tile.Flags);
            }
        foreach (var plug in plugs.Where(p => map.InBounds(p.At)))
        {
            var wall = WallFor(map, plug);
            var at = (plug.At.Y * map.Width + plug.At.X) * 3;
            (bytes[at], bytes[at + 1], bytes[at + 2]) = ((byte)wall.Height, (byte)wall.Material, (byte)wall.Flags);
        }
        return BattleMap.FromBytes(map.Id, map.Width, map.Height, bytes, map.Materials, map.Props, map.Zones);
    }

    /// <summary>
    /// What draws <paramref name="plugs"/> over <paramref name="plan"/>'s world, in its light: each plug's tile as its wall's, built as a block
    /// of one tile is, in painter's order. Empty with no plugs.
    /// </summary>
    public static ImmutableArray<LitItem> Items(WorldPlan plan, ArtIndex art, ImmutableArray<WallPlug> plugs)
    {
        var map = plan.Terrain.Map;
        var inside = plugs.Where(p => map.InBounds(p.At)).ToList();
        if (inside.Count == 0)
            return [];
        var terrain = new BoardTerrain(With(map, inside), null);
        return [.. inside.SelectMany(p => LitBoard.Build(BoardPlan.BuildBlock(terrain, art, new TileRect(p.At.X, p.At.Y, 1, 1), grid: false, world: true), plan.Lighting).Items)
            .OrderBy(i => i.Order)];
    }
}
