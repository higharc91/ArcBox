using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>The map as it is now: base map + height deltas + difficult zones + blocking props. Read-only view.</summary>
public sealed class Terrain
{
    private readonly ImmutableSortedDictionary<Tile, int> _heightDelta;
    private readonly ImmutableSortedSet<Tile> _difficultZones;
    private readonly PropPlacement?[] _propAt;

    /// <summary>Creates a view of <paramref name="map"/> with the given changes applied.</summary>
    public Terrain(BattleMap map, ImmutableSortedDictionary<Tile, int> heightDelta, ImmutableSortedSet<Tile> difficultZones)
    {
        Map = map;
        _heightDelta = heightDelta;
        _difficultZones = difficultZones;
        _propAt = new PropPlacement?[map.Width * map.Height];
        foreach (var prop in map.Props)
        {
            if (!map.InBounds(prop.At))
                continue;
            ref var slot = ref _propAt[prop.At.Y * map.Width + prop.At.X];
            // A blocking prop wins over a non-blocking one on the same tile; otherwise the first listed wins.
            if (slot is null || (prop.Blocks && !slot.Blocks))
                slot = prop;
        }
    }

    /// <summary>The base map.</summary>
    public BattleMap Map { get; }

    /// <summary>The tile's current height: base height plus any delta. Throws outside the map.</summary>
    public int HeightAt(Tile t)
        => Map[t].Height + (_heightDelta.TryGetValue(t, out var delta) ? delta : 0);

    /// <summary>True when a unit may stand on or walk through the tile: in bounds, not impassable, not deep, no blocking prop.</summary>
    public bool Walkable(Tile t)
    {
        if (!Map.InBounds(t))
            return false;
        if ((Map[t].Flags & (TileFlags.Impassable | TileFlags.Deep)) != 0)
            return false;
        return PropAt(t) is not { Blocks: true };
    }

    /// <summary>True when entering the tile costs 2 (a difficult tile or difficult zone). False outside the map.</summary>
    public bool Difficult(Tile t)
        => Map.InBounds(t) && ((Map[t].Flags & TileFlags.Difficult) != 0 || _difficultZones.Contains(t));

    /// <summary>True when the tile is deep. False outside the map.</summary>
    public bool Deep(Tile t) => Map.InBounds(t) && (Map[t].Flags & TileFlags.Deep) != 0;

    /// <summary>The prop standing on the tile, if any.</summary>
    public PropPlacement? PropAt(Tile t) => Map.InBounds(t) ? _propAt[t.Y * Map.Width + t.X] : null;
}
