using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>
/// What a region scene holds, as plain data: its three layers and its markers, read without judging them. A cell is only in a
/// layer if it's painted there, so a tile with no <see cref="Heights"/> cell is at height 0 and one with no <see cref="Flags"/>
/// cell is plain ground. There can be any number of spawns here, since a painter can add any number of markers.
/// </summary>
public sealed record PaintedRegion(string Id, string Name, int Chapter, int Width, int Height)
{
    /// <summary>The tile a region scene is drawn with, in pixels. A flat grid, so a marker's position divides straight into tiles.</summary>
    public const int TilePixels = 32;

    /// <summary>The Material layer.</summary>
    public IReadOnlyDictionary<Tile, string> Materials { get; init; } = ImmutableDictionary<Tile, string>.Empty;

    /// <summary>The Height layer.</summary>
    public IReadOnlyDictionary<Tile, int> Heights { get; init; } = ImmutableDictionary<Tile, int>.Empty;

    /// <summary>The Flags layer.</summary>
    public IReadOnlyDictionary<Tile, TileFlags> Flags { get; init; } = ImmutableDictionary<Tile, TileFlags>.Empty;

    /// <summary>The beat markers.</summary>
    public IReadOnlyList<BeatSpot> Beats { get; init; } = [];

    /// <summary>The stamp markers.</summary>
    public IReadOnlyList<RegionStamp> Stamps { get; init; } = [];

    /// <summary>The tile of every start spawn marker.</summary>
    public IReadOnlyList<Tile> Starts { get; init; } = [];

    /// <summary>The tile of every recovery spawn marker.</summary>
    public IReadOnlyList<Tile> Recoveries { get; init; } = [];

    /// <summary>The tile a scene position falls in. Positions left of or above the origin are negative tiles, not tile 0.</summary>
    public static Tile TileAt(double x, double y) => new((int)Math.Floor(x / TilePixels), (int)Math.Floor(y / TilePixels));

    /// <summary>
    /// The grid to write, and what's wrong with the scene as a region: a height or flags on a tile with no material, which would
    /// otherwise vanish on export, and spawns that aren't exactly one of each. Stamps are recorded over the painted tiles, not
    /// copied, so <see cref="RegionExport"/>'s checks see a stamped tile that was changed.
    /// </summary>
    public (RegionSource Source, ImmutableArray<string> Problems) ToSource()
    {
        var source = new RegionSource(Id, Name, Chapter, Width, Height)
        {
            Start = Starts.FirstOrDefault(),
            Recovery = Recoveries.FirstOrDefault(),
        };
        foreach (var (tile, material) in Materials)
            source.Set(tile, Heights.GetValueOrDefault(tile), material, Flags.GetValueOrDefault(tile));
        source.Beats.AddRange(Beats);
        foreach (var stamp in Stamps)
            source.AddStamp(stamp);

        var problems = new List<string>();
        AddBare(Heights.Keys, "a height");
        AddBare(Flags.Keys, "flags");
        AddSpawn(Starts, "start");
        AddSpawn(Recoveries, "recovery");
        return (source, [.. problems]);

        void AddBare(IEnumerable<Tile> tiles, string what)
        {
            if (tiles.Where(t => !Materials.ContainsKey(t)).OrderBy(t => t.Y).ThenBy(t => t.X).Cast<Tile?>().FirstOrDefault() is { } bare)
                problems.Add($"The tile {bare} has {what} but no material.");
        }

        void AddSpawn(IReadOnlyList<Tile> spawns, string kind)
        {
            if (spawns.Count == 0)
                problems.Add($"The scene has no {kind} spawn.");
            else if (spawns.Count > 1)
                problems.Add($"The scene has {spawns.Count} {kind} spawns, at {string.Join(", ", spawns.SkipLast(1))} and {spawns[^1]}, but needs one.");
        }
    }
}
