using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Where a battle map stands in the world: the stamp of <see cref="Map"/>, whose tile (0, 0) is world tile <see cref="Origin"/>, with
/// <see cref="Height"/> added to its heights. <see cref="Width"/> runs along x and <see cref="Depth"/> along y, as the map's own
/// <see cref="BattleMap.Width"/> and <see cref="BattleMap.Height"/> do. The engine never sees it: a battle plays on the map's own tiles,
/// and this converts between those and the world's for whoever has to put the fight in the world (overworld 5c §1).
/// </summary>
public readonly record struct BattleStage(string Map, Tile Origin, int Height, int Width, int Depth)
{
    /// <summary>
    /// How far from a tile that can't be stood on <see cref="Standable"/> looks for one that can, in steps along x and y. A fight's
    /// tiles match the world's, so this only ever has to step off a tile the world has and the battle didn't (a prop, say).
    /// </summary>
    public const int StandReach = 3;

    /// <summary>The world tile of a battle tile.</summary>
    public Tile ToWorld(Tile battle) => new(battle.X + Origin.X, battle.Y + Origin.Y);

    /// <summary>The battle tile of a world tile, which is off the map for a world tile the stage doesn't hold.</summary>
    public Tile ToBattle(Tile world) => new(world.X - Origin.X, world.Y - Origin.Y);

    /// <summary>True when the world tile is one of the stage's: the map's tile under it is on the map.</summary>
    public bool Holds(Tile world)
        => world.X >= Origin.X && world.Y >= Origin.Y && world.X - Origin.X < Width && world.Y - Origin.Y < Depth;

    /// <summary>
    /// The stage of <paramref name="map"/> in <paramref name="region"/>, or null when the region doesn't stamp it, where the battle
    /// plays on a plain board as it did before the world. The loader allows one stamp per map, so every encounter on a map shares it.
    /// </summary>
    public static BattleStage? Of(WorldRegion region, BattleMap map)
        => region.StampFor(map.Id) is { } stamp ? new BattleStage(map.Id, stamp.At, stamp.Height, map.Width, map.Height) : null;

    /// <summary>
    /// A tile the party can stand on, as near <paramref name="from"/> as can be: <paramref name="from"/> itself if it's walkable, else
    /// the nearest walkable tile at most <see cref="StandReach"/> steps away along x and y (the lowest <see cref="Tile"/> of those
    /// equally near), else the first walkable tile of <paramref name="fallback"/>; null if there's none of those.
    /// </summary>
    public static Tile? Standable(Terrain terrain, Tile from, ImmutableArray<Tile> fallback)
    {
        if (terrain.Walkable(from))
            return from;
        for (var distance = 1; distance <= StandReach; distance++)
        {
            Tile? best = null;
            for (var dx = -distance; dx <= distance; dx++)
            {
                var dy = distance - Math.Abs(dx);
                Tile[] ring = dy == 0 ? [new(from.X + dx, from.Y)] : [new(from.X + dx, from.Y - dy), new(from.X + dx, from.Y + dy)];
                foreach (var t in ring)
                    if (terrain.Walkable(t) && (best is not { } b || t.CompareTo(b) < 0))
                        best = t;
            }
            if (best is not null)
                return best;
        }
        foreach (var t in fallback.IsDefault ? [] : fallback)
            if (terrain.Walkable(t))
                return t;
        return null;
    }
}
