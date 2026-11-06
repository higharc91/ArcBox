using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>One painted tile of a <see cref="RegionSource"/>: a <see cref="TileInfo"/> that names its material.</summary>
public sealed record TileInfoNamed(int Height, string Material, TileFlags Flags);

/// <summary>
/// The editable grid a scene or a draft fills in. It takes what it's given, so a tile outside the region, a height out of range or
/// an unknown material stays here as it is, unclamped, for <see cref="RegionWriter.Write"/> to report. A tile nobody painted has
/// no <see cref="Get"/>.
/// </summary>
public sealed class RegionSource(string id, string name, int chapter, int width, int height)
{
    private readonly Dictionary<Tile, TileInfoNamed> _tiles = [];
    private readonly List<RegionStamp> _stamps = [];

    /// <summary>The region's id.</summary>
    public string Id { get; } = id;

    /// <summary>The region's name.</summary>
    public string Name { get; } = name;

    /// <summary>The chapter the region belongs to.</summary>
    public int Chapter { get; } = chapter;

    /// <summary>Number of columns (X).</summary>
    public int Width { get; } = width;

    /// <summary>Number of rows (Y).</summary>
    public int Height { get; } = height;

    /// <summary>The beat spots.</summary>
    public List<BeatSpot> Beats { get; } = [];

    /// <summary>The props placed in the region.</summary>
    public List<RegionProps> Props { get; } = [];

    /// <summary>The buildings placed in the region.</summary>
    public List<RegionBuilding> Buildings { get; } = [];

    /// <summary>The people standing in the region.</summary>
    public List<RegionPerson> People { get; } = [];

    /// <summary>Where the party starts.</summary>
    public Tile Start { get; set; }

    /// <summary>Where the party recovers after a defeat.</summary>
    public Tile Recovery { get; set; }

    /// <summary>Authored entrance destinations, ordered by name for deterministic export.</summary>
    public SortedDictionary<string, Tile> NamedSpawns { get; } = new(StringComparer.Ordinal);

    /// <summary>The painted tiles.</summary>
    public IReadOnlyDictionary<Tile, TileInfoNamed> Tiles => _tiles;

    /// <summary>The stamps, in the order they were placed.</summary>
    public IReadOnlyList<RegionStamp> Stamps => _stamps;

    /// <summary>Paints a tile, replacing whatever was there.</summary>
    public void Set(Tile t, int height, string material, TileFlags flags) => _tiles[t] = new TileInfoNamed(height, material, flags);

    /// <summary>The painted tile, or null if nothing was painted there.</summary>
    public TileInfoNamed? Get(Tile t) => _tiles.GetValueOrDefault(t);

    /// <summary>
    /// Places a battle map with its tile (0, 0) at <paramref name="at"/>: copies every tile exactly, adding
    /// <paramref name="heightOffset"/> to its height, and records the stamp. Props aren't tiles: a blocking prop's tile is copied as the
    /// plain ground under it, and <see cref="WorldGround"/> stands the map's props back on it.
    /// </summary>
    public void Stamp(BattleMap map, Tile at, int heightOffset)
    {
        foreach (var cell in StampBrush.Cells(map, at, heightOffset))
            Set(cell.At, cell.Height, cell.Material, cell.Flags);
        _stamps.Add(new RegionStamp(map.Id, at, heightOffset));
    }

    /// <summary>
    /// Records a stamp over tiles that are already in the grid, leaving them as they are. A scene uses this: its layers hold the
    /// painted tiles, so copying the map back over them would hide a tile painted over from the checks.
    /// </summary>
    public void AddStamp(RegionStamp stamp) => _stamps.Add(stamp);
}
