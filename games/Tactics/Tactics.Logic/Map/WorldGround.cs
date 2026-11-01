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

    private WorldGround(BattleMap map)
    {
        Map = map;
        Terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
    }

    /// <summary>The whole region as one map.</summary>
    public BattleMap Map { get; }

    /// <summary>The ground as it stands. Nothing changes it: a region has no height deltas, zones or sunk tiles.</summary>
    public Terrain Terrain { get; }

    /// <summary>Builds the ground of <paramref name="region"/>, whose stamps are looked up in <paramref name="maps"/>.</summary>
    public static WorldGround Of(WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps,
        IReadOnlyDictionary<string, PropArtDef>? propDefs = null, IReadOnlyDictionary<string, BuildingArtDef>? buildingDefs = null)
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

        var propsLookup = propDefs ?? ArtManifests.DefaultProps;
        var buildingLookup = buildingDefs ?? ArtManifests.DefaultBuildings;
        List<PropPlacement> allProps =
        [
            .. region.Def.Stamps.Where(s => maps.ContainsKey(s.Map))
                .SelectMany(s => maps[s.Map].Props.Select(p => p with { At = new Tile(p.At.X + s.At.X, p.At.Y + s.At.Y) })),
            .. (region.Def.Props.IsDefault ? [] : region.Def.Props).SelectMany(group =>
            {
                var blocks = propsLookup?.TryGetValue(group.Id, out var def) == true ? def.Blocks : false;
                var cover = propsLookup?.TryGetValue(group.Id, out var def2) == true ? def2.Cover : Cover.None;
                return group.At.Select(t => new PropPlacement(group.Id, t, blocks, cover, group.Variant));
            }),
            .. region.Buildings.SelectMany(b => buildingLookup?.GetValueOrDefault(b.Id) is { } def
                ? def.Blocked(b.At).Where(t => t.X >= 0 && t.Y >= 0 && t.X < width && t.Y < height).Select(t => new PropPlacement(BuildingProp + b.Id, t, true, def.Cover))
                : []),
        ];
        var stampedTiles = region.Def.Stamps.Where(s => maps.ContainsKey(s.Map))
            .SelectMany(s =>
            {
                var map = maps[s.Map];
                return Enumerable.Range(0, map.Height)
                    .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(s.At.X + x, s.At.Y + y)));
            }).ToImmutableHashSet();
        return new WorldGround(BattleMap.FromBytes(region.Def.Id, width, height, bytes, materials, [.. allProps],
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, stampedTiles,
            [.. region.Buildings.Select(b => new BuildingPlacement(b.Id, b.Variant, b.At, region.TileAt(b.At)?.Height ?? 0))]));
    }
}
