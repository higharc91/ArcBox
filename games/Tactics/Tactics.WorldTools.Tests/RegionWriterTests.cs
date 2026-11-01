using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class RegionWriterTests
{
    private const string Grass = "grass/lush", River = "water/river", Ash = "ash/grey";

    private static readonly HashSet<string> Known = [Grass, River, Ash];

    private static readonly Lazy<RulesPack> Base = new(() => RulesPack.Load([ContentPaths.BaseRules]));

    /// <summary>A 2x2 map: heights 1, 1, 2, 1, listing its materials in the other order, with shallow water at (0, 1).</summary>
    private static BattleMap Ford() => BattleMap.FromBytes("ford", 2, 2, (byte[])[1, 1, 0, 1, 1, 0, 2, 0, 1, 1, 1, 0],
        [River, Grass], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);

    private static RegionSource Source(int width = 64, int height = 64) => new("r", "Region", 1, width, height);

    private static RegionDef Written(RegionSource source)
    {
        var result = RegionWriter.Write(source, Known);
        Assert.Empty(result.Problems);
        return Assert.IsType<RegionDef>(result.Region);
    }

    private static WorldRegion Region(RegionSource source) => WorldRegion.From(Written(source));

    /// <summary>Asserts the source won't write, and returns why.</summary>
    private static ImmutableArray<string> Problems(RegionSource source, IReadOnlySet<string>? known = null)
    {
        var result = RegionWriter.Write(source, known ?? Known);
        Assert.Null(result.Region);
        return result.Problems;
    }

    private static string Json(RegionSource source) => JsonSerializer.Serialize(Written(source), ArcJson.Options);

    /// <summary>Puts the region in a temporary pack on top of the base pack and runs <paramref name="load"/> on the pack's folders.</summary>
    private static T WithPack<T>(RegionDef def, Func<string[], T> load)
    {
        var root = Directory.CreateTempSubdirectory("tactics-world-").FullName;
        try
        {
            var dir = Path.Combine(root, "chapter-01");
            Directory.CreateDirectory(Path.Combine(dir, "world"));
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                $$"""{ "id": "chapter-01", "rulesVersion": 1, "requires": ["base"], "files": ["world/{{def.Id}}.json"] }""", utf8);
            File.WriteAllText(Path.Combine(dir, "world", $"{def.Id}.json"), JsonSerializer.Serialize(def, ArcJson.Options), utf8);
            return load([ContentPaths.BaseRules, dir]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Tiles_are_split_into_32_by_32_chunks()
    {
        var source = Source();
        source.Set(new Tile(31, 0), 2, Grass, TileFlags.None);
        source.Set(new Tile(32, 0), 3, River, TileFlags.Water);
        source.Set(new Tile(33, 35), 5, Grass, TileFlags.Hazard);

        var def = Written(source);
        var region = WorldRegion.From(def);

        Assert.Equal([(0, 0), (1, 0), (1, 1)], def.Chunks.Select(c => (c.X, c.Y)));
        Assert.All(def.Chunks, c => Assert.Equal(WorldRegion.ChunkBytes, Convert.FromBase64String(c.Tiles).Length));
        Assert.Equal(new TileInfo(2, 0, TileFlags.None), region.TileAt(new Tile(31, 0)));
        Assert.Equal(new TileInfo(3, 1, TileFlags.Water), region.TileAt(new Tile(32, 0)));
        Assert.Equal(new TileInfo(5, 0, TileFlags.Hazard), region.TileAt(new Tile(33, 35)));
    }

    [Fact]
    public void A_region_keeps_its_names_size_spawns_and_beats()
    {
        var source = new RegionSource("ashen_ford", "Ashen Ford", 1, 160, 120) { Start = new Tile(2, 3), Recovery = new Tile(4, 5) };
        source.Set(new Tile(2, 3), 0, Grass, TileFlags.None);
        source.Beats.Add(new BeatSpot("ch1_old_maud", new Tile(2, 3)));

        var def = Written(source);

        Assert.Equal(("ashen_ford", "Ashen Ford", 1), (def.Id, def.Name, def.Chapter));
        Assert.Equal([160, 120], def.Size);
        Assert.Equal(new RegionSpawns(new Tile(2, 3), new Tile(4, 5)), def.Spawns);
        Assert.Equal([new BeatSpot("ch1_old_maud", new Tile(2, 3))], def.Beats);
        Assert.Empty(def.Edges);
    }

    [Fact]
    public void A_region_with_no_props_loads_as_before()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        var def = Written(source);
        Assert.True(def.Props.IsDefaultOrEmpty);
        var loaded = WithPack(def, dirs => RulesPack.Load(dirs));
        Assert.NotNull(loaded.RegionFor(def.Id));
        Assert.True(loaded.RegionFor(def.Id)!.Def.Props.IsDefaultOrEmpty);
    }

    [Fact]
    public void A_region_with_no_buildings_writes_none()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);

        Assert.DoesNotContain("buildings", JsonSerializer.Serialize(Written(source), ArcJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Buildings_sort_by_where_they_stand_so_a_reexport_is_byte_identical()
    {
        var first = Source();
        first.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        first.Buildings.AddRange([new RegionBuilding("tavern", "red", new Tile(20, 4)), new RegionBuilding("house", "blue", new Tile(3, 9)), new RegionBuilding("cottage", "shakes_oak", new Tile(8, 4))]);
        var second = Source();
        second.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        second.Buildings.AddRange([new RegionBuilding("house", "blue", new Tile(3, 9)), new RegionBuilding("cottage", "shakes_oak", new Tile(8, 4)), new RegionBuilding("tavern", "red", new Tile(20, 4))]);

        var def = Written(first);

        Assert.Equal([new Tile(8, 4), new Tile(20, 4), new Tile(3, 9)], def.Buildings.Select(b => b.At));
        var json = JsonSerializer.Serialize(def, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(Written(second), ArcJson.Options));
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<RegionDef>(json, ArcJson.Options), ArcJson.Options));
    }

    [Fact]
    public void A_repair_keeps_the_buildings()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        source.Buildings.Add(new RegionBuilding("house", "blue", new Tile(3, 9)));

        Assert.Equal(source.Buildings, RegionWriter.Repair(source, new HashSet<string> { Grass }, new Dictionary<Tile, TileInfoNamed>()).Buildings);
    }

    [Fact]
    public void A_props_group_round_trips_byte_identical()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        source.Props.Add(new RegionProps("tree", "autumn", [new Tile(10, 12), new Tile(14, 16)]));
        var def = Written(source);
        var json = JsonSerializer.Serialize(def, ArcJson.Options);
        var deserialized = JsonSerializer.Deserialize<RegionDef>(json, ArcJson.Options);
        var jsonAgain = JsonSerializer.Serialize(deserialized, ArcJson.Options);
        Assert.Equal(json, jsonAgain);
    }

    [Fact]
    public void Props_sort_by_id_variant_and_tile()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        source.Props.Add(new RegionProps("tree", "green", [new Tile(20, 20), new Tile(10, 10)]));
        source.Props.Add(new RegionProps("pine", null, [new Tile(5, 5), new Tile(2, 2)]));
        source.Props.Add(new RegionProps("tree", "autumn", [new Tile(8, 8)]));
        var def = Written(source);
        Assert.Equal(3, def.Props.Length);
        Assert.Equal(("pine", (string?)null), (def.Props[0].Id, def.Props[0].Variant));
        Assert.Equal([new Tile(2, 2), new Tile(5, 5)], def.Props[0].At);
        Assert.Equal(("tree", (string?)"autumn"), (def.Props[1].Id, def.Props[1].Variant));
        Assert.Equal([new Tile(8, 8)], def.Props[1].At);
        Assert.Equal(("tree", (string?)"green"), (def.Props[2].Id, def.Props[2].Variant));
        Assert.Equal([new Tile(10, 10), new Tile(20, 20)], def.Props[2].At);
    }

    [Fact]
    public void A_chunk_with_no_tiles_is_left_out()
    {
        var source = Source(128, 64);
        source.Set(new Tile(70, 40), 0, Grass, TileFlags.None);

        Assert.Equal([(2, 1)], Written(source).Chunks.Select(c => (c.X, c.Y)));
    }

    [Fact]
    public void Every_unpainted_tile_in_a_listed_chunk_is_impassable()
    {
        var source = Source();
        source.Set(new Tile(5, 5), 0, Grass, TileFlags.None);

        var region = Region(source);

        var ground = WorldGround.Of(region, new Dictionary<string, BattleMap>(), ArtManifest.None).Terrain;

        Assert.True(ground.Walkable(new Tile(5, 5)));
        Assert.Equal(new TileInfo(0, 0, TileFlags.Impassable), region.TileAt(new Tile(5, 6)));
        Assert.False(ground.Walkable(new Tile(5, 6)));
        Assert.Null(region.TileAt(new Tile(32, 0)));
    }

    [Fact]
    public void Tiles_of_a_chunk_past_the_regions_edge_are_written_blank_and_read_as_void()
    {
        var source = Source(33, 33);
        source.Set(new Tile(32, 32), 0, Grass, TileFlags.None);

        var region = Region(source);

        Assert.NotNull(region.TileAt(new Tile(32, 32)));
        Assert.Null(region.TileAt(new Tile(33, 32)));
    }

    [Fact]
    public void A_beat_on_an_unpainted_tile_cant_be_reached()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, Grass, TileFlags.None);
        source.Recovery = new Tile(0, 0);
        source.Beats.Add(new BeatSpot("ch1_hole", new Tile(1, 0)));

        var errors = WithPack(Written(source), dirs => Assert.Throws<ContentException>(() => RulesPack.Load(dirs)).Errors);

        Assert.Equal("The beat 'ch1_hole' at (1, 0) can't be reached from the start.", Assert.Single(errors).Message);
    }

    [Fact]
    public void Materials_are_listed_once_in_ordinal_name_order()
    {
        var source = Source();
        source.Set(new Tile(40, 0), 0, River, TileFlags.None);
        source.Set(new Tile(2, 1), 0, Ash, TileFlags.None);
        source.Set(new Tile(0, 1), 0, Grass, TileFlags.None);
        source.Set(new Tile(1, 1), 0, Ash, TileFlags.None);

        // By name, not by first use: where a material first appears must not decide every tile's index, or a small paint
        // change could renumber a whole chunk.
        Assert.Equal([Ash, Grass, River], Written(source).Materials);
    }

    [Fact]
    public void Tiles_index_the_sorted_material_list()
    {
        var source = Source();
        source.Set(new Tile(0, 0), 0, River, TileFlags.None);
        source.Set(new Tile(1, 0), 0, Ash, TileFlags.None);

        var region = Region(source);

        Assert.Equal(River, region.Def.Materials[region.TileAt(new Tile(0, 0))!.Material]);
        Assert.Equal(Ash, region.Def.Materials[region.TileAt(new Tile(1, 0))!.Material]);
    }

    [Fact]
    public void Painting_a_tile_again_replaces_it()
    {
        var source = Source();
        source.Set(new Tile(1, 1), 3, Ash, TileFlags.Water);
        source.Set(new Tile(1, 1), 2, Grass, TileFlags.None);

        Assert.Equal(new TileInfoNamed(2, Grass, TileFlags.None), source.Get(new Tile(1, 1)));
        Assert.Equal([Grass], Written(source).Materials);
    }

    [Fact]
    public void Getting_an_unpainted_tile_is_null()
        => Assert.Null(Source().Get(new Tile(1, 1)));

    [Fact]
    public void Stamp_copies_the_map_with_its_height_offset()
    {
        var source = Source();

        source.Stamp(Ford(), new Tile(4, 5), 1);

        Assert.Equal(new TileInfoNamed(2, Grass, TileFlags.None), source.Get(new Tile(4, 5)));
        Assert.Equal(new TileInfoNamed(2, Grass, TileFlags.None), source.Get(new Tile(5, 5)));
        Assert.Equal(new TileInfoNamed(3, River, TileFlags.Water), source.Get(new Tile(4, 6)));
        Assert.Equal(new TileInfoNamed(2, Grass, TileFlags.None), source.Get(new Tile(5, 6)));
        Assert.Null(source.Get(new Tile(6, 5)));
        Assert.Equal([new RegionStamp("ford", new Tile(4, 5), 1)], Written(source).Stamps);
    }

    [Fact]
    public void A_stamp_pushing_a_tile_above_9_is_a_problem_not_a_clamp()
    {
        var source = Source();

        source.Stamp(Ford(), new Tile(4, 5), 8);

        Assert.Equal(10, source.Get(new Tile(4, 6))!.Height);
        Assert.Equal(["The tile (4, 6) has height 10, but heights go from 0 to 9. Repaint the Height layer, or change the stamp's HeightOffset."], Problems(source));
    }

    [Fact]
    public void A_stamp_pushing_a_tile_below_0_is_a_problem()
    {
        var source = Source();

        source.Stamp(Ford(), new Tile(4, 5), -2);

        Assert.Equal(["3 tiles, x 4 to 5, y 5 to 6, first (4, 5), have heights outside 0 to 9, the first at -1. Repaint the Height layer, or change the stamp's HeightOffset."], Problems(source));
    }

    [Theory]
    [InlineData(63, 5, "2 tiles, x 64 to 64, y 5 to 6, first (64, 5), are outside the 64x64 region. Erase what's painted there, or change Size on the Region node.")]
    [InlineData(5, 63, "2 tiles, x 5 to 6, y 64 to 64, first (5, 64), are outside the 64x64 region. Erase what's painted there, or change Size on the Region node.")]
    [InlineData(-1, 5, "2 tiles, x -1 to -1, y 5 to 6, first (-1, 5), are outside the 64x64 region. Erase what's painted there, or change Size on the Region node.")]
    [InlineData(5, -1, "2 tiles, x 5 to 6, y -1 to -1, first (5, -1), are outside the 64x64 region. Erase what's painted there, or change Size on the Region node.")]
    public void A_tile_outside_the_region_is_a_problem(int x, int y, string problem)
    {
        var source = Source();

        source.Stamp(Ford(), new Tile(x, y), 0);

        Assert.Equal([problem], Problems(source));
    }

    [Fact]
    public void Columns_outside_the_region_are_one_line_with_a_count_and_box()
    {
        var source = Source(40, 32);
        for (var y = 0; y < 120; y++)
            for (var x = 40; x < 45; x++)
                source.Set(new Tile(x, y), 0, Grass, TileFlags.None);

        Assert.Equal(["600 tiles, x 40 to 44, y 0 to 119, first (40, 0), are outside the 40x32 region. Erase what's painted there, or change Size on the Region node."],
            Problems(source));
    }

    [Fact]
    public void Repair_drops_what_cant_be_written_and_keeps_the_rest()
    {
        var source = Source(8, 8);
        source.Set(new Tile(1, 1), 3, Grass, TileFlags.Water);
        source.Set(new Tile(2, 1), 2, "lava/hot", TileFlags.None);
        source.Set(new Tile(3, 1), 12, Grass, (TileFlags)64 | TileFlags.Hazard);
        source.Set(new Tile(9, 1), 0, Grass, TileFlags.None);
        source.Beats.Add(new BeatSpot("b", new Tile(1, 1)));
        source.AddStamp(new RegionStamp("m", new Tile(0, 0), 0));

        var repaired = RegionWriter.Repair(source, Known, new Dictionary<Tile, TileInfoNamed>());

        Assert.Empty(RegionWriter.Write(repaired, Known).Problems);
        Assert.Equal(new TileInfoNamed(3, Grass, TileFlags.Water), repaired.Get(new Tile(1, 1)));
        Assert.Equal(2, repaired.Get(new Tile(2, 1))!.Height);
        Assert.Contains(repaired.Get(new Tile(2, 1))!.Material, Known);
        Assert.Equal(new TileInfoNamed(9, Grass, TileFlags.Hazard), repaired.Get(new Tile(3, 1)));
        Assert.Null(repaired.Get(new Tile(9, 1)));
        Assert.Equal(source.Beats, repaired.Beats);
        Assert.Equal(source.Stamps, repaired.Stamps);
    }

    [Fact]
    public void Repair_takes_a_stamped_tiles_material_and_height_from_its_map()
    {
        var source = Source(8, 8);
        source.Set(new Tile(1, 1), 3, "lava/hot", TileFlags.None);
        source.Set(new Tile(2, 1), 12, Grass, TileFlags.None);

        var repaired = RegionWriter.Repair(source, Known, new Dictionary<Tile, TileInfoNamed> { [new Tile(1, 1)] = new(1, River, TileFlags.Water), [new Tile(2, 1)] = new(1, River, TileFlags.Water) });

        Assert.Equal(new TileInfoNamed(3, River, TileFlags.None), repaired.Get(new Tile(1, 1)));
        Assert.Equal(new TileInfoNamed(1, Grass, TileFlags.None), repaired.Get(new Tile(2, 1)));
    }

    [Fact]
    public void An_unknown_material_is_a_problem_naming_the_tile()
    {
        var source = Source();
        source.Set(new Tile(4, 9), 0, Grass, TileFlags.None);
        source.Set(new Tile(7, 9), 0, "lava/hot", TileFlags.None);

        Assert.Equal(["The tile (7, 9) uses the material 'lava/hot', which isn't in the palette or a stamped map. Repaint it from the palette."], Problems(source));
    }

    [Fact]
    public void Each_unknown_material_is_a_problem_once()
    {
        var source = Source();
        foreach (var (x, material) in new[] { (5, "lava/hot"), (6, "lava/hot"), (7, "ice/blue") })
            source.Set(new Tile(x, 0), 0, material, TileFlags.None);

        Assert.Equal(
            [
                "2 tiles, x 5 to 6, y 0 to 0, first (5, 0), use the material 'lava/hot', which isn't in the palette or a stamped map. Repaint them from the palette.",
                "The tile (7, 0) uses the material 'ice/blue', which isn't in the palette or a stamped map. Repaint it from the palette.",
            ],
            Problems(source));
    }

    [Fact]
    public void A_stamped_material_is_checked_too()
    {
        var source = Source();
        source.Stamp(Ford(), new Tile(4, 5), 0);

        Assert.Equal(["3 tiles, x 4 to 5, y 5 to 6, first (4, 5), use the material 'grass/lush', which isn't in the palette or a stamped map. Repaint them from the palette."],
            Problems(source, new HashSet<string> { River }));
    }

    [Fact]
    public void Undefined_flag_bits_are_a_problem()
    {
        var source = Source();
        source.Set(new Tile(1, 2), 0, Grass, (TileFlags)64 | TileFlags.Water);

        Assert.Equal(["The tile (1, 2) has undefined flag bits 0x40. Repaint it on the Flags layer."], Problems(source));
    }

    [Fact]
    public void More_materials_than_a_byte_can_index_is_a_problem()
    {
        var names = Enumerable.Range(0, 257).Select(i => $"grass/v{i}").ToHashSet();
        var source = Source();
        foreach (var (name, i) in names.Select((name, i) => (name, i)))
            source.Set(new Tile(i % 32, i / 32), 0, name, TileFlags.None);

        Assert.Equal(["The region uses 257 materials, but a tile can only index 256. Repaint some tiles so it uses fewer."], Problems(source, names));
    }

    [Fact]
    public void Writing_twice_gives_identical_output()
    {
        var source = Source();
        source.Set(new Tile(40, 40), 2, Ash, TileFlags.Difficult);
        source.Stamp(Ford(), new Tile(4, 5), 1);

        Assert.Equal(Json(source), Json(source));
    }

    [Fact]
    public void The_order_tiles_are_painted_in_doesnt_change_the_output()
    {
        var one = Source();
        one.Set(new Tile(40, 40), 2, Ash, TileFlags.Difficult);
        one.Set(new Tile(1, 1), 1, Grass, TileFlags.None);
        one.Set(new Tile(40, 1), 0, River, TileFlags.Water);
        var other = Source();
        other.Set(new Tile(40, 1), 0, River, TileFlags.Water);
        other.Set(new Tile(1, 1), 1, Grass, TileFlags.None);
        other.Set(new Tile(40, 40), 2, Ash, TileFlags.Difficult);

        Assert.Equal(Json(one), Json(other));
    }

    [Fact]
    public void The_written_region_loads_and_passes_the_checks()
    {
        var map = Base.Value.MapFor("river_crossing")!;
        var (width, height) = (map.Width + 8, map.Height + 8);
        var source = new RegionSource("r", "Region", 1, width, height) { Recovery = new Tile(1, 0) };
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                source.Set(new Tile(x, y), 0, Grass, TileFlags.None);
        source.Stamp(map, new Tile(4, 4), 0);
        source.Beats.Add(new BeatSpot("ch1_ember", new Tile(0, 2)));

        // Start at (0, 0) and a stamp height of 0 are the zero values a loader with required fields would refuse if left out.
        var result = RegionWriter.Write(source, new HashSet<string>([Grass, .. map.Materials]));
        Assert.Empty(result.Problems);
        var pack = WithPack(result.Region!, dirs => RulesPack.Load(dirs));

        var region = pack.RegionFor("r")!;
        Assert.Equal(map[new Tile(3, 2)].Height, region.TileAt(new Tile(7, 6))!.Height);
        Assert.Equal(map[new Tile(3, 2)].Flags, region.TileAt(new Tile(7, 6))!.Flags);
        Assert.Equal(new RegionStamp("river_crossing", new Tile(4, 4), 0), Assert.Single(region.Def.Stamps));
    }
}
