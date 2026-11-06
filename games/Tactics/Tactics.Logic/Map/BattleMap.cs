using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Terrain properties of one tile. Combine with bitwise OR.</summary>
[Flags]
public enum TileFlags
{
    /// <summary>Plain ground.</summary>
    None = 0,
    /// <summary>Water.</summary>
    Water = 1,
    /// <summary>Costs 2 Move to enter.</summary>
    Difficult = 2,
    /// <summary>Cannot be entered.</summary>
    Impassable = 4,
    /// <summary>Harms whoever stands on it (rules decide how).</summary>
    Hazard = 8,
    /// <summary>A fall into it downs a party member or kills an enemy; cannot be entered on foot.</summary>
    Deep = 16,
}

/// <summary>How much of a target a prop hides.</summary>
public enum Cover
{
    /// <summary>No cover.</summary>
    None,
    /// <summary>Half cover.</summary>
    Half,
    /// <summary>Three-quarters cover.</summary>
    ThreeQuarters,
    /// <summary>Total cover.</summary>
    Total,
}

/// <summary>One tile of a base map: height in levels, an index into <see cref="BattleMap.Materials"/>, and its flags.</summary>
public sealed record TileInfo(int Height, int Material, TileFlags Flags);

/// <summary>An object standing on a tile. A blocking prop cannot be walked into; any prop may give cover.</summary>
public sealed record PropPlacement(string Id, Tile At, bool Blocks, Cover Cover, string? Variant = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Requires = default,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Unless = default)
{
    public bool Equals(PropPlacement? other) => other is not null
        && Id == other.Id && At == other.At && Blocks == other.Blocks && Cover == other.Cover && Variant == other.Variant
        && (Requires.IsDefaultOrEmpty ? other.Requires.IsDefaultOrEmpty : !other.Requires.IsDefaultOrEmpty && Requires.SequenceEqual(other.Requires))
        && (Unless.IsDefaultOrEmpty ? other.Unless.IsDefaultOrEmpty : !other.Unless.IsDefaultOrEmpty && Unless.SequenceEqual(other.Unless));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(At); hash.Add(Blocks); hash.Add(Cover); hash.Add(Variant);
        if (!Requires.IsDefaultOrEmpty) foreach (var flag in Requires) hash.Add(flag);
        hash.Add(0);
        if (!Unless.IsDefaultOrEmpty) foreach (var flag in Unless) hash.Add(flag);
        return hash.ToHashCode();
    }

    /// <summary>Whether this placement has story gates; ordinary props keep their legacy behavior.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Conditional => !Requires.IsDefaultOrEmpty || !Unless.IsDefaultOrEmpty;

    /// <summary>All required flags must hold, and no excluded flag may hold.</summary>
    public bool Present(IReadOnlySet<string>? flags = null)
        => (Requires.IsDefaultOrEmpty || Requires.All(f => flags?.Contains(f) == true))
        && (Unless.IsDefaultOrEmpty || Unless.All(f => flags?.Contains(f) != true));
}

/// <summary>
/// A building standing on the map, as the world has it. A region's alone has any. It's read for drawing: what it blocks is on the map's props.
/// </summary>
/// <param name="Id">Its id in <c>town/manifest.json</c>.</param>
/// <param name="Variant">Its variant.</param>
/// <param name="At">Its back corner tile.</param>
/// <param name="Height">The height of the ground at that tile, which it stands on.</param>
public sealed record BuildingPlacement(string Id, string Variant, Tile At, int Height);

/// <summary>An immutable base map: a grid of tiles, its materials, props and named zones (e.g. spawn areas).</summary>
public sealed class BattleMap
{
    private readonly TileInfo[] _tiles;

    private BattleMap(string id, int width, int height, TileInfo[] tiles, ImmutableArray<string> materials,
        ImmutableArray<PropPlacement> props, ImmutableSortedDictionary<string, ImmutableArray<Tile>> zones,
        ImmutableHashSet<Tile>? stamped = null, ImmutableArray<BuildingPlacement> buildings = default)
    {
        Id = id;
        Width = width;
        Height = height;
        _tiles = tiles;
        Materials = materials;
        Props = props;
        Zones = zones;
        StampedTiles = stamped ?? [];
        Buildings = buildings.IsDefault ? [] : buildings;
    }

    /// <summary>The map's id.</summary>
    public string Id { get; }

    /// <summary>Number of columns (X).</summary>
    public int Width { get; }

    /// <summary>Number of rows (Y).</summary>
    public int Height { get; }

    /// <summary>The material names that <see cref="TileInfo.Material"/> indexes.</summary>
    public ImmutableArray<string> Materials { get; }

    /// <summary>The props on the map.</summary>
    public ImmutableArray<PropPlacement> Props { get; }

    /// <summary>Named tile sets, ordered by ordinal name.</summary>
    public ImmutableSortedDictionary<string, ImmutableArray<Tile>> Zones { get; }

    /// <summary>Tiles that are part of a stamp on this map, if any.</summary>
    public ImmutableHashSet<Tile> StampedTiles { get; }

    /// <summary>The buildings on the map: a region's, or none.</summary>
    public ImmutableArray<BuildingPlacement> Buildings { get; }

    /// <summary>The same terrain with only the scenery present for these story flags.</summary>
    public BattleMap WithStoryFlags(IReadOnlySet<string> flags) => WithProps([.. Props.Where(p => p.Present(flags))]);

    /// <summary>The same map with different props, sharing its tiles, stamps and buildings.</summary>
    internal BattleMap WithProps(ImmutableArray<PropPlacement> props) => new(Id, Width, Height, _tiles, Materials, props, Zones, StampedTiles, Buildings);

    /// <summary>True when the tile lies on the grid.</summary>
    public bool InBounds(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height;

    /// <summary>The tile's info. Throws <see cref="ArgumentOutOfRangeException"/> outside the grid.</summary>
    public TileInfo this[Tile t]
        => InBounds(t) ? _tiles[t.Y * Width + t.X] : throw new ArgumentOutOfRangeException(nameof(t), t, "Tile is outside the map.");

    /// <summary>
    /// Builds a map from raw bytes: three per tile (height, material index, flags), row-major, so the tile at
    /// (x, y) starts at byte <c>(y * w + x) * 3</c>.
    /// </summary>
    public static BattleMap FromBytes(string id, int w, int h, ReadOnlySpan<byte> tiles, ImmutableArray<string> materials,
        ImmutableArray<PropPlacement> props, ImmutableSortedDictionary<string, ImmutableArray<Tile>> zones,
        ImmutableHashSet<Tile>? stamped = null, ImmutableArray<BuildingPlacement> buildings = default)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentOutOfRangeException(nameof(w), "A map needs a positive size.");
        if (tiles.Length != w * h * 3)
            throw new ArgumentException($"Expected {w * h * 3} bytes for a {w}x{h} map, got {tiles.Length}.", nameof(tiles));

        var infos = new TileInfo[w * h];
        for (var i = 0; i < infos.Length; i++)
        {
            var material = tiles[i * 3 + 1];
            if (material >= materials.Length)
                throw new ArgumentException($"Tile {i} uses material {material} but there are {materials.Length} materials.", nameof(tiles));
            infos[i] = new TileInfo(tiles[i * 3], material, (TileFlags)tiles[i * 3 + 2]);
        }

        return new BattleMap(id, w, h, infos, materials, props, zones.WithComparers(StringComparer.Ordinal), stamped, buildings);
    }
}
