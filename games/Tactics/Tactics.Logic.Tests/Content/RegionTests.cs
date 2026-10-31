using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class RegionTests
{
    private const string RegionFile = "chapter-01/world/r.json";
    private const int TilesInChunk = 32 * 32;

    /// <summary>A chunk's base64, every tile blank but the given ones, which are in the chunk's own coordinates.</summary>
    internal static string Tiles(params (int X, int Y, byte Height, byte Material, TileFlags Flags)[] tiles)
    {
        var bytes = new byte[TilesInChunk * 3];
        foreach (var (x, y, height, material, flags) in tiles)
        {
            var at = (x + y * 32) * 3;
            (bytes[at], bytes[at + 1], bytes[at + 2]) = (height, material, (byte)flags);
        }
        return Convert.ToBase64String(bytes);
    }

    private static RegionDef Region(int width, int height, params RegionChunk[] chunks)
        => new("r", "Region", 1, [width, height], ["grass/lush", "water/river"], [.. chunks], [], [],
            new RegionSpawns(new Tile(0, 0), new Tile(1, 0)), []);

    private static string Json(RegionDef def) => JsonSerializer.Serialize(def, ArcJson.Options);

    private static IReadOnlyList<ContentError> ErrorsOf(string json)
        => ValidationTests.ErrorsOfChapter(("world/r.json", json));

    private static IReadOnlyList<ContentError> ErrorsOf(RegionDef def) => ErrorsOf(Json(def));

    [Fact]
    public void Tile_lookups_cross_chunks()
    {
        var region = WorldRegion.From(Region(64, 32, new RegionChunk(0, 0, Tiles((31, 0, 2, 0, TileFlags.None))),
            new RegionChunk(1, 0, Tiles((0, 0, 3, 1, TileFlags.Water), (1, 0, 5, 1, TileFlags.Difficult)))));

        Assert.Equal(new TileInfo(2, 0, TileFlags.None), region.TileAt(new Tile(31, 0)));
        Assert.Equal(new TileInfo(3, 1, TileFlags.Water), region.TileAt(new Tile(32, 0)));
        Assert.Equal(new TileInfo(5, 1, TileFlags.Difficult), region.TileAt(new Tile(33, 0)));
    }

    [Fact]
    public void A_tile_reads_its_row_as_well_as_its_column()
    {
        var region = WorldRegion.From(Region(64, 64, new RegionChunk(1, 1, Tiles((3, 7, 4, 1, TileFlags.Hazard)))));

        Assert.Equal(new TileInfo(4, 1, TileFlags.Hazard), region.TileAt(new Tile(35, 39)));
        Assert.Equal(new TileInfo(0, 0, TileFlags.None), region.TileAt(new Tile(39, 35)));
    }

    [Fact]
    public void A_missing_chunk_is_void()
    {
        var region = WorldRegion.From(Region(64, 32, new RegionChunk(0, 0, Tiles())));

        Assert.Null(region.TileAt(new Tile(32, 0)));
        Assert.False(region.Walkable(new Tile(32, 0)));
        Assert.True(region.Walkable(new Tile(31, 0)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(40, 0)]     // past the width, in a chunk that goes on
    [InlineData(0, 20)]     // past the height, likewise
    [InlineData(64, 0)]     // past the last chunk
    public void A_tile_outside_the_region_is_void(int x, int y)
    {
        var region = WorldRegion.From(Region(40, 20, new RegionChunk(0, 0, Tiles()), new RegionChunk(1, 0, Tiles())));

        Assert.Null(region.TileAt(new Tile(x, y)));
        Assert.False(region.Walkable(new Tile(x, y)));
    }

    [Fact]
    public void Impassable_and_deep_tiles_cant_be_walked()
    {
        var region = WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, Tiles(
            (1, 0, 0, 0, TileFlags.Impassable), (2, 0, 0, 0, TileFlags.Deep | TileFlags.Water),
            (3, 0, 0, 0, TileFlags.Water | TileFlags.Difficult | TileFlags.Hazard)))));

        Assert.True(region.Walkable(new Tile(0, 0)));
        Assert.False(region.Walkable(new Tile(1, 0)));
        Assert.False(region.Walkable(new Tile(2, 0)));
        Assert.True(region.Walkable(new Tile(3, 0)));   // shallow water, difficult ground and hazards can all be walked
    }

    [Fact]
    public void A_region_loads_from_its_world_file()
    {
        var def = Region(32, 32, new RegionChunk(0, 0, Tiles((1, 2, 3, 1, TileFlags.Water), (4, 5, 2, 0, TileFlags.None)))) with
        {
            Stamps = [new RegionStamp("post", new Tile(4, 5), 2)],
            Beats = [new BeatSpot("ch1_old_maud", new Tile(6, 7))],
            Edges = [new RegionEdge("skarrow_hills", "east", 12)],
        };

        var pack = ValidationTests.LoadChapter(("world/r.json", Json(def)),
            ("maps/post.json", """{ "id": "post", "size": [1, 1], "tiles": "AAAA", "materials": ["grass/lush"], "props": [], "zones": {} }"""));

        Assert.Equal(["r"], pack.Regions.Keys);
        Assert.Same(StringComparer.Ordinal, pack.Regions.KeyComparer);
        var region = pack.RegionFor("r")!;
        Assert.Same(pack.Regions["r"], region);
        Assert.Equal(new TileInfo(3, 1, TileFlags.Water), region.TileAt(new Tile(1, 2)));
        Assert.Equal(("r", "Region", 1, new RegionSpawns(new Tile(0, 0), new Tile(1, 0))),
            (region.Def.Id, region.Def.Name, region.Def.Chapter, region.Def.Spawns));
        Assert.Equal(def.Size, region.Def.Size);
        Assert.Equal(def.Materials, region.Def.Materials);
        Assert.Equal(def.Chunks, region.Def.Chunks);
        Assert.Equal(def.Stamps, region.Def.Stamps);
        Assert.Equal(def.Beats, region.Def.Beats);
        Assert.Equal(def.Edges, region.Def.Edges);
    }

    [Fact]
    public void A_pack_with_no_regions_has_none_and_an_unknown_id_is_null()
    {
        var pack = RulesPack.Load([ContentPaths.Fixture("minimal", "base")]);

        Assert.Empty(pack.Regions);
        Assert.Null(pack.RegionFor("nowhere"));
        Assert.Empty(RulesPack.InMemory("h").Regions);
    }

    [Fact]
    public void A_regions_bytes_are_in_the_content_hash()
    {
        var one = ValidationTests.LoadChapter(("world/r.json", Json(Region(32, 32, new RegionChunk(0, 0, Tiles())))));
        var other = ValidationTests.LoadChapter(("world/r.json", Json(Region(32, 32, new RegionChunk(0, 0, Tiles((0, 0, 1, 0, TileFlags.None)))))));

        Assert.NotEqual(one.Hash, other.Hash);
    }

    [Fact]
    public void A_chunk_with_the_wrong_length_is_a_content_error()
    {
        var shortTiles = Convert.ToBase64String(new byte[TilesInChunk * 3 - 3]);

        Assert.Equal([new ContentError(RegionFile, "/chunks/0/tiles", "Expected 3072 tile bytes for a chunk (32x32, 3 per tile), got 3069.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, shortTiles))));
    }

    [Fact]
    public void A_chunk_that_isnt_base64_is_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/0/tiles", "The tiles aren't valid base64.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, "not base64!"))));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(134217728, 0)]  // 32 times this wraps round to 0 in an int
    public void A_chunk_outside_the_region_is_a_content_error(int x, int y)
        => Assert.Equal([new ContentError(RegionFile, "/chunks/0", $"Chunk ({x}, {y}) is outside the 32x32 region.")],
            ErrorsOf(Region(32, 32, new RegionChunk(x, y, Tiles()))));

    [Fact]
    public void A_chunk_partly_inside_the_region_is_allowed()
    {
        var def = Region(33, 33, new RegionChunk(1, 1, Tiles())) with { Spawns = new RegionSpawns(new Tile(32, 32), new Tile(32, 32)) };
        var pack = ValidationTests.LoadChapter(("world/r.json", Json(def)));

        Assert.True(pack.RegionFor("r")!.Walkable(new Tile(32, 32)));
    }

    [Fact]
    public void Two_chunks_in_one_place_are_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/1", "Chunk (0, 0) is listed twice.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles()), new RegionChunk(0, 0, Tiles()))));

    [Fact]
    public void A_height_above_9_is_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/1/tiles", "Tile (33, 2) has height 10, above the limit of 9.")],
            ErrorsOf(Region(64, 32, new RegionChunk(0, 0, Tiles((0, 0, 9, 0, TileFlags.None))),
                new RegionChunk(1, 0, Tiles((1, 2, 10, 0, TileFlags.None))))));

    [Fact]
    public void A_material_the_region_doesnt_list_is_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/0/tiles", "Tile (1, 0) uses material 2, but the region has 2 materials.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles((1, 0, 0, 2, TileFlags.None))))));

    [Fact]
    public void Undefined_flag_bits_are_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/0/tiles", "Tile (0, 1) has undefined flag bits 0x40.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles((0, 1, 0, 0, (TileFlags)0x41))))));

    [Fact]
    public void One_chunk_reports_each_kind_of_bad_tile_once()
    {
        var errors = ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles(
            (0, 0, 10, 0, TileFlags.None), (1, 0, 11, 0, TileFlags.None), (2, 0, 0, 9, TileFlags.None), (3, 0, 0, 0, (TileFlags)0x80)))));

        Assert.Equal(
            [
                "Tile (0, 0) has height 10, above the limit of 9.",
                "Tile (2, 0) uses material 9, but the region has 2 materials.",
                "Tile (3, 0) has undefined flag bits 0x80.",
            ], errors.Select(e => e.Message));
    }

    [Theory]
    [InlineData(new[] { 32 })]
    [InlineData(new[] { 32, 32, 32 })]
    [InlineData(new[] { 0, 32 })]
    [InlineData(new[] { 32, -1 })]
    public void A_bad_size_is_a_content_error(int[] size)
        => Assert.Equal([new ContentError(RegionFile, "/size", "The size must be [w, h], both at least 1.")],
            ErrorsOf(Region(32, 32) with { Size = [.. size] }));

    [Fact]
    public void An_unknown_key_in_a_region_is_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/colour", "Unknown property 'colour'.")],
            ErrorsOf(Json(Region(32, 32))[..^1] + """, "colour": "red" }"""));

    [Fact]
    public void An_unknown_key_inside_a_chunk_is_a_content_error()
        => Assert.Equal([new ContentError(RegionFile, "/chunks/0/tile", "Unknown property 'tile'.")],
            ErrorsOf("""
                { "id": "r", "name": "R", "chapter": 1, "size": [32, 32], "materials": [], "stamps": [], "beats": [], "edges": [],
                  "spawns": { "start": [0, 0], "recovery": [0, 0] }, "chunks": [ { "x": 0, "y": 0, "tiles": "", "tile": "" } ] }
                """));

    [Theory]
    [InlineData("""{ "start": [0, 0] }""", "'recovery'")]
    [InlineData("""{ "recovery": [0, 0] }""", "'start'")]
    public void A_spawn_left_out_is_a_content_error_not_tile_0_0(string spawns, string missing)
    {
        var error = Assert.Single(ErrorsOf($$"""
            { "id": "r", "name": "R", "chapter": 1, "size": [32, 32], "materials": [], "chunks": [], "stamps": [], "beats": [], "edges": [],
              "spawns": {{spawns}} }
            """));

        Assert.Equal((RegionFile, "/spawns"), (error.File, error.Pointer));
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void A_region_with_no_spawns_is_a_content_error()
    {
        var json = """
            { "id": "r", "name": "R", "chapter": 1, "size": [32, 32], "materials": [], "chunks": [], "stamps": [], "beats": [], "edges": [] }
            """;

        Assert.Equal([new ContentError(RegionFile, "/spawns", "'spawns' is missing.")], ErrorsOf(json));
    }

    [Fact]
    public void Two_regions_with_one_id_are_a_content_error()
    {
        var region = Json(Region(32, 32, new RegionChunk(0, 0, Tiles())));

        var error = Assert.Single(ValidationTests.ErrorsOfChapter(("world/a.json", region), ("world/b.json", region)));

        Assert.Equal(("chapter-01/world/b.json", "/id"), (error.File, error.Pointer));
        Assert.StartsWith("Duplicate region 'r'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_world_file_must_sit_directly_in_the_world_folder()
    {
        var error = Assert.Single(ValidationTests.ErrorsOfChapter(("world/deep/r.json", Json(Region(32, 32)))));

        Assert.Equal("chapter-01/manifest.json", error.File);
        Assert.Contains("isn't a known content file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void From_refuses_a_bad_size_a_repeated_chunk_and_bad_chunk_bytes()
    {
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, "AAAA"))));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32) with { Size = [32] }));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, Tiles()), new RegionChunk(0, 0, Tiles()))));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, Tiles((0, 0, 0, 2, TileFlags.None))))));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, "not base64!"))));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, Tiles((0, 0, 10, 0, TileFlags.None))))));
        Assert.Throws<ArgumentException>(() => WorldRegion.From(Region(32, 32, new RegionChunk(0, 0, Tiles((0, 0, 0, 0, (TileFlags)0x40))))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_chapter_below_1_is_a_content_error(int chapter)
        => Assert.Equal([new ContentError(RegionFile, "/chapter", $"The chapter must be at least 1, got {chapter}.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles())) with { Chapter = chapter }));

    [Theory]
    [InlineData("up")]
    [InlineData("East")]
    [InlineData("")]
    public void A_side_that_isnt_a_compass_point_is_a_content_error(string side)
        => Assert.Equal([new ContentError(RegionFile, "/edges/0/side", $"The side '{side}' must be north, east, south or west.")],
            ErrorsOf(Region(32, 32, new RegionChunk(0, 0, Tiles())) with { Edges = [new RegionEdge("skarrow_hills", side, 0)] }));
}
