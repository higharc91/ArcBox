using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A region as the party walks it: one <see cref="BattleMap"/> of the whole region, with its stamped maps' props standing at world tiles,
/// its buildings' blocking tiles as props of their own, and impassable void wherever nothing was painted, so <see cref="Paths.StepCost"/> and <see cref="Terrain.Walkable"/> work on it
/// unchanged. Stamped tiles need no copying, as the region's own tiles match the map (the loader checks that). A stamp on an unknown map
/// adds no props, which only a pack that failed to load can have.
/// </summary>
public sealed class WorldGround
{
    /// <summary>What void is made of, added to the region's materials if it doesn't list it.</summary>
    private const string VoidMaterial = "void/deep";

    /// <summary>A tile's material is one byte.</summary>
    private const int MaxMaterials = 256;

    /// <summary>What marks a building's blocking tile among the props, so it's never taken for a prop with the same name. No art has this id, so none is drawn.</summary>
    private const string BuildingProp = "building:";

    private WorldGround(BattleMap map, ImmutableArray<RegionPerson> people)
    {
        Map = map;
        People = people;
        Terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
    }

    /// <summary>The people the TV draws. They don't touch the ground: nothing here reads them.</summary>
    public ImmutableArray<RegionPerson> People { get; }

    /// <summary>The whole region as one map.</summary>
    public BattleMap Map { get; }

    /// <summary>The ground as it stands. Nothing changes it: a region has no height deltas, zones or sunk tiles.</summary>
    public Terrain Terrain { get; }

    /// <summary>The id of the blocking prop that stands for a thing in the way (<see cref="With"/>). Only the server's walking sees it.</summary>
    public const string ThingProp = "interactable";

    /// <summary>
    /// The ground with something in the way on each of <paramref name="blocked"/>: a closed door, a chest, a trap the party has seen
    /// (interactables design §2.4). Walking, reach, landing and the checks use this over the plain ground; the plain ground stays the region
    /// with every passage open, which is how it's painted. With nothing blocked it's <see cref="Terrain"/> itself.
    /// </summary>
    public Terrain With(IReadOnlyCollection<Tile> blocked)
    {
        if (blocked.Count == 0)
            return Terrain;
        ImmutableArray<PropPlacement> props = [.. Map.Props, .. blocked.Where(Map.InBounds).Select(t => new PropPlacement(ThingProp, t, true, Cover.None))];
        return new Terrain(Map.WithProps(props), ImmutableSortedDictionary<Tile, int>.Empty, []);
    }

    /// <summary>True when <paramref name="region"/> holds what only art defines, so its ground can't be built without an <see cref="ArtManifest"/>.</summary>
    public static bool NeedsArt(RegionDef region) => !region.Props.IsDefaultOrEmpty || !region.Buildings.IsDefaultOrEmpty;

    /// <summary>
    /// Builds the ground of <paramref name="region"/>, whose stamps are looked up in <paramref name="maps"/>. What its props and buildings block comes from
    /// <paramref name="art"/>, and throws <see cref="ArgumentException"/> for one that isn't there: the server and the TV have to agree on every tile, so a missing definition
    /// can't be taken to mean walkable.
    /// Roaming arenas listed in <paramref name="naturalArenas"/> keep the region's terrain art and blending;
    /// their map tiles, props, collisions and battle-stage bounds are unchanged.
    /// </summary>
    public static WorldGround Of(WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps, ArtManifest art,
        IReadOnlyCollection<string>? naturalArenas = null)
    {
        var (width, height) = (region.Def.Size[0], region.Def.Size[1]);
        var materials = region.Def.Materials;
        var voidMaterial = materials.IndexOf(VoidMaterial);
        if (voidMaterial < 0 && materials.Length < MaxMaterials)
        {
            voidMaterial = materials.Length;
            materials = materials.Add(VoidMaterial);
        }

        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 3;
                var tile = region.TileAt(new Tile(x, y));
                (bytes[at], bytes[at + 1], bytes[at + 2]) = tile is null
                    ? ((byte)0, (byte)Math.Max(voidMaterial, 0), (byte)TileFlags.Impassable)
                    : ((byte)tile.Height, (byte)tile.Material, (byte)tile.Flags);
            }

        List<PropPlacement> allProps =
        [
            .. region.Def.Stamps.Where(s => maps.ContainsKey(s.Map))
                .SelectMany(s => maps[s.Map].Props.Select(p => p with { At = new Tile(p.At.X + s.At.X, p.At.Y + s.At.Y) })),
            .. (region.Def.Props.IsDefault ? [] : region.Def.Props).SelectMany(group =>
            {
                if (!art.Props.TryGetValue(group.Id, out var def))
                    throw new ArgumentException($"Region '{region.Def.Id}' has the prop '{group.Id}', which the art manifest doesn't define.", nameof(art));
                return group.At.Select(t => new PropPlacement(group.Id, t, def.Blocks, def.Cover, group.Variant));
            }),
            .. region.Buildings.SelectMany(b =>
            {
                if (!art.Buildings.TryGetValue(b.Id, out var def))
                    throw new ArgumentException($"Region '{region.Def.Id}' has the building '{b.Id}', which the art manifest doesn't define.", nameof(art));
                return def.Blocked(b.At).Where(t => t.X >= 0 && t.Y >= 0 && t.X < width && t.Y < height).Select(t => new PropPlacement(BuildingProp + b.Id, t, true, def.Cover));
            }),
        ];
        var stampedTiles = region.Def.Stamps.Where(s => maps.ContainsKey(s.Map) && naturalArenas?.Contains(s.Map) != true)
            .SelectMany(s =>
            {
                var map = maps[s.Map];
                return Enumerable.Range(0, map.Height)
                    .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(s.At.X + x, s.At.Y + y)));
            }).ToImmutableHashSet();
        return new WorldGround(BattleMap.FromBytes(region.Def.Id, width, height, bytes, materials, [.. allProps],
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, stampedTiles,
            [.. region.Buildings.Select(b => new BuildingPlacement(b.Id, b.Variant, b.At, region.TileAt(b.At)?.Height ?? 0))]), region.People);
    }
}
