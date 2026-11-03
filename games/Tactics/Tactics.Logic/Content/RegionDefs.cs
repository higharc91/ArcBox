using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// A region of the overworld as written in <c>world/&lt;id&gt;.json</c>: <see cref="Size"/> is <c>[w, h]</c> in tiles,
/// <see cref="Materials"/> is what a tile's material byte indexes, and a chunk that isn't listed is void. A region has its own
/// material list, so compare a stamped map's materials by name. <see cref="WorldRegion.From"/> builds the engine's region.
/// </summary>
public sealed record RegionDef(string Id, string Name, [property: JsonRequired] int Chapter, ImmutableArray<int> Size,
    ImmutableArray<string> Materials, ImmutableArray<RegionChunk> Chunks, ImmutableArray<RegionStamp> Stamps,
    ImmutableArray<BeatSpot> Beats, RegionSpawns Spawns, ImmutableArray<RegionEdge> Edges,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<RegionProps> Props = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<RegionBuilding> Buildings = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<RegionPerson> People = default);

/// <summary>
/// A group of props of the same <see cref="Id"/> and optional <see cref="Variant"/> standing at world tiles <see cref="At"/>.
/// </summary>
public sealed record RegionProps(string Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Variant, ImmutableArray<Tile> At);

/// <summary>
/// A building of <c>town/manifest.json</c> standing in the region. <see cref="At"/> is the back corner tile of its footprint: the smallest x and y it covers.
/// </summary>
public sealed record RegionBuilding(string Id, string Variant, [property: JsonRequired] Tile At);

/// <summary>
/// Someone the TV draws standing in the region: dressing, with no state on the server, who blocks nobody. <see cref="Preset"/> is a <c>characters/presets</c> id, <see cref="Face"/>
/// one of <see cref="Faces"/>, and <see cref="Walk"/> the tiles of a short loop they walk from <see cref="At"/> and back to it, if they walk.
/// </summary>
public sealed record RegionPerson(string Preset, [property: JsonRequired] Tile At, string Face,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<Tile> Walk = default)
{
    /// <summary>The ways someone can face, as the screen has them: +x is south-east and +y south-west.</summary>
    public static readonly ImmutableArray<string> Faces = ["se", "sw", "nw", "ne"];

    /// <summary>The most tiles a walk can have: it's someone pacing about, not a journey.</summary>
    public const int MostSteps = 12;
}

/// <summary>
/// One chunk: <see cref="X"/> and <see cref="Y"/> count chunks, not tiles, and <see cref="Tiles"/> is base64 of three bytes per
/// tile for <see cref="WorldRegion.ChunkSize"/> × <see cref="WorldRegion.ChunkSize"/> tiles, row-major (height, material index,
/// flags), as on a battle map.
/// </summary>
public sealed record RegionChunk([property: JsonRequired] int X, [property: JsonRequired] int Y, string Tiles);

/// <summary>
/// A battle map placed in the world: the map's tile (0, 0) is world tile <see cref="At"/>, and <see cref="Height"/> is added to
/// its heights. It names no encounter, because encounters share maps: the arc's battle beat picks one.
/// </summary>
public sealed record RegionStamp(string Map, [property: JsonRequired] Tile At, [property: JsonRequired] int Height);

/// <summary>The world tile of a beat in <c>lore/arcs.json</c>.</summary>
public sealed record BeatSpot(string Beat, [property: JsonRequired] Tile At);

/// <summary>Where the party starts in a region, and where it recovers.</summary>
public sealed record RegionSpawns([property: JsonRequired] Tile Start, [property: JsonRequired] Tile Recovery);

/// <summary>
/// Where a region meets its neighbour: the region it joins and on which side. <see cref="Offset"/> is how far along the shared
/// side the neighbour's tile 0 sits from this region's tile 0, so the neighbour's facing edge carries the negated value: if one
/// says 5, the other says -5.
/// </summary>
public sealed record RegionEdge(string Neighbour, string Side, [property: JsonRequired] int Offset);
