using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>A node that's in the wrong place in a region scene: <paramref name="Fix"/> is what to do about it, with no full stop.</summary>
public sealed record StrayNode(string Node, string Parent, string Fix, Tile At);

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

    /// <summary>The Props layer: a prop id and its variant on each painted tile.</summary>
    public IReadOnlyDictionary<Tile, (string Id, string? Variant)> Props { get; init; } = ImmutableDictionary<Tile, (string Id, string? Variant)>.Empty;

    /// <summary>The building markers, each on its footprint's back corner.</summary>
    public IReadOnlyList<RegionBuilding> Buildings { get; init; } = [];

    /// <summary>The person markers.</summary>
    public IReadOnlyList<RegionPerson> People { get; init; } = [];

    /// <summary>The beat markers.</summary>
    public IReadOnlyList<BeatSpot> Beats { get; init; } = [];

    /// <summary>The stamp markers.</summary>
    public IReadOnlyList<RegionStamp> Stamps { get; init; } = [];

    /// <summary>The tile of every start spawn marker.</summary>
    public IReadOnlyList<Tile> Starts { get; init; } = [];

    /// <summary>The tile of every recovery spawn marker.</summary>
    public IReadOnlyList<Tile> Recoveries { get; init; } = [];

    /// <summary>Named markers, kept as a list so duplicate names are reported rather than overwritten.</summary>
    public IReadOnlyList<(string Name, Tile At)> NamedSpawns { get; init; } = [];

    /// <summary>The layers and marker groups that have been moved off the origin, which would shift what's drawn but not what's exported.</summary>
    public IReadOnlyList<string> MovedNodes { get; init; } = [];

    /// <summary>Nodes the export ignores because of where they are, or what they are.</summary>
    public IReadOnlyList<StrayNode> Strays { get; init; } = [];

    /// <summary>Where stamps used to be: areas that may still hold a map's tiles, until the painter says they're repainted.</summary>
    public IReadOnlyList<RegionStamp> OldStamps { get; init; } = [];

    /// <summary>The scene's file name without its extension, or null if it has none yet.</summary>
    public string? SceneFile { get; init; }

    /// <summary>The tile a scene position falls in. Positions left of or above the origin are negative tiles, not tile 0.</summary>
    public static Tile TileAt(double x, double y) => new((int)Math.Floor(x / TilePixels), (int)Math.Floor(y / TilePixels));

    /// <summary>
    /// The grid to write, and what's wrong with the scene as a region. Each kind of bad tile is one line with its count and place. A
    /// tile with no material in a chunk that has some is a problem, since it would play as an invisible wall; a whole empty chunk is
    /// void, which is allowed. Stamps are recorded over the painted tiles, not copied, so <see cref="RegionExport"/>'s checks see a
    /// stamped tile that was changed.
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
        source.Buildings.AddRange(Buildings);
        source.People.AddRange(People);
        foreach (var stamp in Stamps)
            source.AddStamp(stamp);
        foreach (var group in Props.GroupBy(p => (p.Value.Id, p.Value.Variant)))
            source.Props.Add(new RegionProps(group.Key.Id, group.Key.Variant, [.. group.Select(p => p.Key).Distinct().Order()]));

        var problems = new List<string>();
        foreach (var (name, at) in NamedSpawns)
            if (string.IsNullOrWhiteSpace(name) || name is "start" or "recovery")
                problems.Add($"The named spawn at {at} needs a name other than start or recovery.");
            else if (!source.NamedSpawns.TryAdd(name, at))
                problems.Add($"The named spawn '{name}' is repeated. Give each destination a unique name.");
        if (SceneFile is { } file && file != Id)
            problems.Add($"The scene is named '{file}' but its region Id is '{Id}', and two scenes with one Id would overwrite each other's file. Rename the scene or change Id.");
        problems.AddRange(MovedNodes.Select(node => $"The {node} node has been moved, so its markers aren't on the tiles they show. Set its Position back to zero."));
        problems.AddRange(Strays.Select(s => $"The {s.Node} at {s.At} is under {s.Parent}, so it isn't exported. {s.Fix}."));
        AddBare(Heights.Keys, "a height", "Height");
        AddBare(Flags.Keys, "flags", "Flags");
        AddBare(Props.Keys, "a prop", "Props");
        AddHoles();
        AddSpawn(Starts, "start", "Start");
        AddSpawn(Recoveries, "recovery", "Recovery");
        AddBeats();
        return (source, [.. problems]);

        void AddBare(IEnumerable<Tile> tiles, string what, string layer)
        {
            var bare = tiles.Where(t => !Materials.ContainsKey(t)).ToList();
            if (bare.Count > 0)
                problems.Add($"{TileSpread.Say(bare, "has", "have")} {what} but no material. Paint the Material layer there, or erase the {layer}.");
        }

        void AddHoles()
        {
            var holes = new List<Tile>();
            var chunks = Materials.Keys.Where(t => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height)
                .Select(t => (X: t.X / WorldRegion.ChunkSize, Y: t.Y / WorldRegion.ChunkSize)).Distinct();
            foreach (var (chunkX, chunkY) in chunks)
                for (var y = chunkY * WorldRegion.ChunkSize; y < Math.Min(Height, (chunkY + 1) * WorldRegion.ChunkSize); y++)
                    for (var x = chunkX * WorldRegion.ChunkSize; x < Math.Min(Width, (chunkX + 1) * WorldRegion.ChunkSize); x++)
                        if (!Materials.ContainsKey(new Tile(x, y)))
                            holes.Add(new Tile(x, y));
            if (holes.Count > 0)
                problems.Add($"{TileSpread.Say(holes, "has", "have")} no material in a painted chunk, which plays as an invisible wall. "
                    + $"Paint {TileSpread.It(holes)}, and add the Impassable flag if a wall is what you want.");
        }

        void AddSpawn(IReadOnlyList<Tile> spawns, string kind, string value)
        {
            if (spawns.Count == 0)
                problems.Add($"The scene has no {kind} spawn. Add a SpawnMarker under Spawns and set Kind to {value}.");
            else if (spawns.Count > 1)
                problems.Add($"The scene has {spawns.Count} {kind} spawns, at {string.Join(", ", spawns.SkipLast(1))} and {spawns[^1]}, but needs one. "
                    + $"Delete the {(spawns.Count == 2 ? "extra" : "extras")}.");
        }

        void AddBeats()
        {
            foreach (var unnamed in Beats.Where(b => b.Beat.Length == 0))
                problems.Add($"The beat marker at {unnamed.At} has no Beat id. Set its Beat, or delete it.");
            foreach (var repeat in Beats.Where(b => b.Beat.Length > 0).GroupBy(b => b.Beat, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                var spots = repeat.Select(b => b.At.ToString()).ToList();
                problems.Add($"The beat '{repeat.Key}' has {spots.Count} markers, at {string.Join(", ", spots.SkipLast(1))} and {spots[^1]}. "
                    + $"Delete the {(spots.Count == 2 ? "extra" : "extras")}.");
            }
        }
    }
}
