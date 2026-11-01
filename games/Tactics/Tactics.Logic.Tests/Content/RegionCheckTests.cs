using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class RegionCheckTests
{
    private const string RegionFile = "chapter-01/world/r.json";

    // The region lists its materials in the other order, so the indexes differ where the names agree.
    private const byte Grass = 0, River = 1;
    private const string FordMaterials = """["water/river", "grass/lush"]""";

    private static readonly string FordMap = MapJson("ford", 2, 2, FordMaterials, [1, 1, 0, 1, 1, 0, 2, 0, 1, 1, 1, 0]);

    /// <summary>The map <c>ford</c> stamped at (4, 5) with its heights raised by 1.</summary>
    private static readonly (int X, int Y, byte Height, byte Material, TileFlags Flags)[] FordTiles =
    [
        (4, 5, 2, Grass, TileFlags.None), (5, 5, 2, Grass, TileFlags.None),
        (4, 6, 3, River, TileFlags.Water), (5, 6, 2, Grass, TileFlags.None),
    ];

    private static readonly RegionStamp FordStamp = new("ford", new Tile(4, 5), 1);

    private static string MapJson(string id, int width, int height, string materials, byte[] tiles, string zones = "{}")
        => $$"""{ "id": "{{id}}", "size": [{{width}}, {{height}}], "tiles": "{{Convert.ToBase64String(tiles)}}", "materials": {{materials}}, "props": [], "zones": {{zones}} }""";

    private static RegionDef Region(string id, params (int X, int Y, byte Height, byte Material, TileFlags Flags)[] tiles)
        => new(id, id, 1, [32, 32], ["grass/lush", "water/river"], [new RegionChunk(0, 0, RegionTests.Tiles(tiles))], [], [],
            new RegionSpawns(new Tile(0, 0), new Tile(1, 0)), []);

    private static RegionDef Region(params (int X, int Y, byte Height, byte Material, TileFlags Flags)[] tiles) => Region("r", tiles);

    private static RegionDef Stamped(params RegionStamp[] stamps) => Region(FordTiles) with { Stamps = [.. stamps] };

    /// <summary>A wall of impassable tiles down column 8: everything east of it is cut off from the start.</summary>
    private static (int X, int Y, byte Height, byte Material, TileFlags Flags)[] Wall(TileFlags flags = TileFlags.Impassable)
        => [.. Enumerable.Range(0, 32).Select(y => (8, y, (byte)0, Grass, flags))];

    private static (string, string) World(RegionDef def) => ($"world/{def.Id}.json", JsonSerializer.Serialize(def, ArcJson.Options));

    private static (string, string) Map(string id, string json) => ($"maps/{id}.json", json);

    private static readonly ArtManifest Art = ArtManifests.Load(ContentPaths.ContentBase);

    private static IReadOnlyList<ContentError> ErrorsOf(RegionDef def) => ValidationTests.ErrorsOfChapter(Art, World(def), Map("ford", FordMap));

    private static void Loads(params RegionDef[] defs)
        => Assert.NotNull(ValidationTests.LoadChapter(Art, [.. defs.Select(World), Map("ford", FordMap)]));

    [Fact]
    public void A_stamp_that_matches_its_map_loads_though_its_materials_are_listed_in_another_order()
        => Loads(Stamped(FordStamp));

    [Theory]
    [InlineData(4, 6, 2, River, TileFlags.Water, "height 2, the map has 3")]
    [InlineData(4, 6, 3, Grass, TileFlags.Water, "material 'grass/lush', the map has 'water/river'")]
    [InlineData(4, 6, 3, River, TileFlags.None, "flags None, the map has Water")]
    [InlineData(5, 6, 3, River, TileFlags.Water, "height 3, the map has 2; material 'water/river', the map has 'grass/lush'; flags Water, the map has None")]
    public void A_stamp_that_differs_from_its_map_is_an_error(int x, int y, byte height, byte material, TileFlags flags, string difference)
    {
        var tiles = FordTiles.Where(t => (t.X, t.Y) != (x, y)).Append((x, y, height, material, flags)).ToArray();

        Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", $"The stamp 'ford' doesn't match its map at ({x}, {y}): the region has {difference}.")],
            ErrorsOf(Region(tiles) with { Stamps = [FordStamp] }));
    }

    [Fact]
    public void A_stamp_over_void_is_an_error()
    {
        var def = Region((31, 5, 2, Grass, TileFlags.None), (31, 6, 3, River, TileFlags.Water)) with
        {
            Size = [64, 32],
            Stamps = [new RegionStamp("ford", new Tile(31, 5), 1)],
        };

        Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", "The stamp 'ford' doesn't match its map at (32, 5): the region has void there. 1 more tile differs, x 32 to 32, y 5 to 6.")],
            ErrorsOf(def));
    }

    [Fact]
    public void A_stamp_reports_its_first_bad_tile_and_how_many_more()
    {
        var error = Assert.Single(ErrorsOf(Stamped(FordStamp) with { Chunks = [new RegionChunk(0, 0, RegionTests.Tiles())] }));

        Assert.Equal("The stamp 'ford' doesn't match its map at (4, 5): the region has height 0, the map has 2. 3 more tiles differ, x 4 to 5, y 5 to 6.", error.Message);
    }

    [Fact]
    public void A_stamp_on_an_unknown_map_is_an_error()
        => Assert.Equal([new ContentError(RegionFile, "/stamps/0/map", "Unknown map 'nowhere'.")],
            ErrorsOf(Stamped(new RegionStamp("nowhere", new Tile(4, 5), 1))));

    [Fact]
    public void A_stamp_on_a_map_that_failed_to_load_reports_only_the_map()
    {
        var errors = ValidationTests.ErrorsOfChapter(World(Stamped(new RegionStamp("bad", new Tile(4, 5), 1))),
            Map("bad", """{ "id": "bad", "size": [1, 1], "tiles": "not base64!", "materials": ["grass/lush"], "props": [], "zones": {} }"""));

        Assert.Equal("chapter-01/maps/bad.json", Assert.Single(errors).File);
    }

    [Fact]
    public void Two_stamps_of_one_map_are_an_error()
        => Assert.Equal(
            [new ContentError(RegionFile, "/stamps/1/map", "The map 'ford' is stamped twice, but an encounter on it must resolve to one place.")],
            ErrorsOf(Region([.. FordTiles, .. FordTiles.Select(t => (t.X + 10, t.Y, t.Height, t.Material, t.Flags))]) with
            {
                Stamps = [FordStamp, new RegionStamp("ford", new Tile(14, 5), 1)],
            }));

    [Theory]
    [InlineData(31, 5)]             // one column over the east side
    [InlineData(4, 31)]             // one row over the south side
    [InlineData(-1, 5)]
    [InlineData(4, -1)]
    [InlineData(int.MaxValue, 5)]   // its far corner wraps round in an int
    public void A_stamp_outside_the_region_is_an_error(int x, int y)
        => Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", $"The stamp 'ford' at ({x}, {y}) is 2x2, which doesn't fit in the 32x32 region.")],
            ErrorsOf(Stamped(new RegionStamp("ford", new Tile(x, y), 1))));

    [Fact]
    public void A_spot_or_spawn_that_cant_be_reached_is_an_error()
    {
        var def = Region(Wall()) with
        {
            Spawns = new RegionSpawns(new Tile(0, 0), new Tile(10, 10)),
            Beats = [new BeatSpot("ch1_ember", new Tile(7, 9)), new BeatSpot("ch1_old_maud", new Tile(9, 9)), new BeatSpot("ch1_wall", new Tile(8, 2))],
        };

        Assert.Equal(
            [
                new ContentError(RegionFile, "/spawns/recovery", "The recovery spawn at (10, 10) can't be reached from the start."),
                new ContentError(RegionFile, "/beats/1/at", "The beat 'ch1_old_maud' at (9, 9) can't be reached from the start."),
                new ContentError(RegionFile, "/beats/2/at", "The beat 'ch1_wall' at (8, 2) can't be reached from the start."),
            ],
            ErrorsOf(def));
    }

    [Fact]
    public void Two_markers_on_one_cut_off_tile_are_each_reported_once()
    {
        var def = Region(Wall()) with
        {
            Beats = [new BeatSpot("ch1_a", new Tile(9, 9)), new BeatSpot("ch1_b", new Tile(9, 9))],
        };

        Assert.Equal(["/beats/0/at", "/beats/1/at"], ErrorsOf(def).Select(e => e.Pointer));
    }

    [Fact]
    public void Deep_water_cuts_a_region_in_two()
        => Assert.Equal("The beat 'b' at (9, 9) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region(Wall(TileFlags.Deep | TileFlags.Water)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] })).Message);

    [Fact]
    public void Shallow_water_can_be_crossed()
        => Loads(Region(Wall(TileFlags.Water | TileFlags.Difficult)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] });

    [Fact]
    public void A_marker_in_void_cant_be_reached()
        => Assert.Equal("The beat 'b' at (40, 0) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region() with { Size = [64, 32], Beats = [new BeatSpot("b", new Tile(40, 0))] })).Message);

    [Fact]
    public void A_marker_outside_the_region_cant_be_reached()
        => Assert.Equal("The beat 'b' at (-1, 0) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region() with { Beats = [new BeatSpot("b", new Tile(-1, 0))] })).Message);

    [Fact]
    public void A_corner_cant_be_cut()
        => Assert.Equal("The beat 'b' at (1, 1) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region((1, 0, 0, Grass, TileFlags.Impassable), (0, 1, 0, Grass, TileFlags.Impassable)) with
            {
                Spawns = new RegionSpawns(new Tile(0, 0), new Tile(0, 0)),
                Beats = [new BeatSpot("b", new Tile(1, 1))],
            })).Message);

    [Fact]
    public void A_step_of_2_levels_can_be_climbed_both_ways()
        => Loads(Region("up", Plain(8, 31, 2)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] },
            Region("down", Plain(0, 7, 2)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] });

    [Fact]
    public void A_step_of_3_levels_cant_be_climbed()
        => Assert.Equal("The beat 'b' at (9, 9) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region(Plain(8, 31, 3)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] })).Message);

    [Fact]
    public void A_drop_of_3_levels_can_be_walked_down()
        => Loads(Region(Plain(0, 7, 3)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] });

    [Fact]
    public void A_drop_of_any_height_can_be_walked_down()
        => Loads(Region(Plain(0, 7, 9)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] });

    [Fact]
    public void The_climb_back_up_a_drop_cant_be_walked()
        => Assert.Equal("The beat 'b' at (0, 0) can't be reached from the start.", Assert.Single(
            ErrorsOf(Region(Plain(0, 7, 3)) with
            {
                Spawns = new RegionSpawns(new Tile(9, 9), new Tile(10, 9)),
                Beats = [new BeatSpot("b", new Tile(0, 0))],
            })).Message);

    [Fact]
    public void A_marker_reached_only_through_a_drop_can_be_reached()
    {
        // Columns 0 to 7 stand 3 levels up, and a wall down column 9 leaves column 8 as the foot of the drop and nothing else.
        var def = Region([.. Plain(0, 7, 3), .. Enumerable.Range(0, 32).Select(y => (9, y, (byte)0, Grass, TileFlags.Impassable))]) with
        {
            Spawns = new RegionSpawns(new Tile(0, 0), new Tile(1, 0)),
            Beats = [new BeatSpot("foot", new Tile(8, 31))],
        };

        Loads(def);
    }

    /// <summary>Whole columns of plain ground at one height.</summary>
    private static (int X, int Y, byte Height, byte Material, TileFlags Flags)[] Plain(int firstColumn, int lastColumn, byte height)
        =>
        [
            .. from x in Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
               from y in Enumerable.Range(0, 32)
               select (x, y, height, Grass, TileFlags.None),
        ];

    [Fact]
    public void A_start_that_cant_be_stood_on_is_reported_alone()
    {
        var def = Region((0, 0, 0, Grass, TileFlags.Impassable)) with { Beats = [new BeatSpot("b", new Tile(9, 9))] };

        Assert.Equal([new ContentError(RegionFile, "/spawns/start", "The start spawn at (0, 0) can't be stood on.")], ErrorsOf(def));
    }

    [Fact]
    public void A_stamp_that_cant_be_reached_is_an_error()
    {
        var ring = from x in Enumerable.Range(3, 4)
                   from y in Enumerable.Range(4, 4)
                   where x is 3 or 6 || y is 4 or 7
                   select (x, y, (byte)0, Grass, TileFlags.Impassable);

        Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", "The stamp 'ford' at (4, 5) can't be reached from the start.")],
            ErrorsOf(Region([.. FordTiles, .. ring]) with { Stamps = [FordStamp] }));
    }

    [Fact]
    public void One_tile_of_a_stamp_is_enough_to_reach_it()
    {
        var ring = from x in Enumerable.Range(3, 4)
                   from y in Enumerable.Range(4, 4)
                   where (x is 3 or 6 || y is 4 or 7) && (x, y) != (4, 4)
                   select (x, y, (byte)0, Grass, TileFlags.Impassable);

        Loads(Region([.. FordTiles, .. ring]) with { Stamps = [FordStamp] });
    }

    /// <summary>
    /// A 4x1 map stamped at (4, 5). Its second tile is impassable and the region walls in the two tiles east of it, so only
    /// map tile (0, 0) can be walked to.
    /// </summary>
    private static IReadOnlyList<ContentError> ErrorsOfGate(string zones)
        => ValidationTests.ErrorsOfChapter(GateFiles(zones));

    private static (string, string)[] GateFiles(string zones)
    {
        var map = MapJson("gate", 4, 1, FordMaterials, [0, 1, 0, 0, 1, 4, 0, 1, 0, 0, 1, 0], zones);
        var def = Region(
            (5, 5, 0, Grass, TileFlags.Impassable), (6, 4, 0, Grass, TileFlags.Impassable), (7, 4, 0, Grass, TileFlags.Impassable),
            (8, 5, 0, Grass, TileFlags.Impassable), (6, 6, 0, Grass, TileFlags.Impassable), (7, 6, 0, Grass, TileFlags.Impassable)) with
        {
            Stamps = [new RegionStamp("gate", new Tile(4, 5), 0)],
        };
        return [World(def), Map("gate", map)];
    }

    [Fact]
    public void A_party_spawn_tile_that_cant_be_reached_is_an_error()
        => Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", "The party spawn of the stamp 'gate' at (6, 5) can't be reached from the start.")],
            ErrorsOfGate("""{ "party_spawn": [[2, 0]] }"""));

    [Fact]
    public void Only_the_first_party_spawn_tile_that_cant_be_reached_is_reported()
        => Assert.Equal(
            [new ContentError(RegionFile, "/stamps/0", "The party spawn of the stamp 'gate' at (7, 5) can't be reached from the start.")],
            ErrorsOfGate("""{ "party_spawn": [[0, 0], [3, 0], [2, 0]] }"""));

    [Fact]
    public void A_party_spawn_that_can_be_reached_loads_though_other_tiles_of_the_stamp_cant()
        => Assert.NotNull(ValidationTests.LoadChapter(GateFiles("""{ "party_spawn": [[0, 0]] }""")));

    [Fact]
    public void A_map_with_other_zones_keeps_the_any_tile_rule()
        => Assert.NotNull(ValidationTests.LoadChapter(GateFiles("""{ "nave": [[2, 0]] }""")));

    [Fact]
    public void An_empty_party_spawn_keeps_the_any_tile_rule()
        => Assert.NotNull(ValidationTests.LoadChapter(GateFiles("""{ "party_spawn": [] }""")));

    [Fact]
    public void A_marker_only_reachable_through_a_prop_is_an_error()
    {
        // A 3x1 corridor whose middle tile holds a crate, walled so the crate is the only way to its east end.
        const string door = """{ "id": "door", "size": [3, 1], "tiles": "AAEAAAEAAAEA", "materials": ["water/river", "grass/lush"], "props": [{ "id": "crate", "at": [1, 0], "blocks": true, "cover": "half" }], "zones": {} }""";
        var def = Region(
            (5, 4, 0, Grass, TileFlags.Impassable), (6, 4, 0, Grass, TileFlags.Impassable), (7, 5, 0, Grass, TileFlags.Impassable),
            (5, 6, 0, Grass, TileFlags.Impassable), (6, 6, 0, Grass, TileFlags.Impassable)) with
        {
            Stamps = [new RegionStamp("door", new Tile(4, 5), 0)],
            Beats = [new BeatSpot("b", new Tile(6, 5))],
        };

        Assert.Equal(
            [new ContentError(RegionFile, "/beats/0/at", "The beat 'b' at (6, 5) can't be reached from the start.")],
            ValidationTests.ErrorsOfChapter(World(def), Map("door", door)));
    }

    /// <summary>A whole chunk 3 levels up with a one-tile pit at (9, 9) that can be dropped into and not climbed out of.</summary>
    private static (int X, int Y, byte Height, byte Material, TileFlags Flags)[] Pit()
        => [.. Plain(0, 31, 3).Where(t => (t.X, t.Y) != (9, 9)), (9, 9, 0, Grass, TileFlags.None)];

    /// <summary>A beaten party wakes here whatever the story's order, so it's the one marker the loader can say this about.</summary>
    [Fact]
    public void A_pocket_you_cant_walk_back_from_is_an_error()
        => Assert.Equal(
            [new ContentError(RegionFile, "/spawns/recovery", "The recovery spawn at (9, 9) can't walk back to the start.")],
            ErrorsOf(Region(Pit()) with { Spawns = new RegionSpawns(new Tile(0, 0), new Tile(9, 9)) }));

    /// <summary>The loader can't read lore, so only <c>StoryOrder</c> can tell a story that ends in the pit from one that needs to leave it.</summary>
    [Fact]
    public void A_story_can_end_in_a_pit_whichever_order_the_beats_are_listed()
    {
        BeatSpot pit = new("in_the_pit", new Tile(9, 9)), far = new("far", new Tile(20, 20));

        Loads(Region(Pit()) with { Beats = [far, pit] });
        Loads(Region(Pit()) with { Beats = [pit, far] });
    }

    [Fact]
    public void A_stamp_in_a_pit_is_left_to_the_story()
    {
        var map = MapJson("hole", 1, 1, FordMaterials, [0, 1, 0], """{ "party_spawn": [[0, 0]] }""");
        var def = Region(Pit()) with { Stamps = [new RegionStamp("hole", new Tile(9, 9), 0)], Beats = [new BeatSpot("far", new Tile(20, 20))] };

        Assert.NotNull(ValidationTests.LoadChapter(World(def), Map("hole", map)));
    }

    /// <summary>
    /// The story never sends anyone in, so this one is <c>StoryOrder</c>'s warning at export, where a painter part way through still gets
    /// the file, rather than a pack the game refuses.
    /// </summary>
    [Fact]
    public void A_pit_with_no_marker_in_it_loads()
        => Loads(Region(Pit()) with { Beats = [new BeatSpot("far", new Tile(20, 20))] });

    [Fact]
    public void Two_stamps_that_overlap_are_an_error()
    {
        var other = MapJson("bank", 1, 1, FordMaterials, [2, 1, 0]);
        var def = Stamped(FordStamp, new RegionStamp("bank", new Tile(5, 6), 0));

        Assert.Equal(
            [new ContentError(RegionFile, "/stamps/1", "The stamp 'bank' at (5, 6) overlaps the stamp 'ford' at (4, 5).")],
            ValidationTests.ErrorsOfChapter(World(def), Map("ford", FordMap), Map("bank", other)));
    }

    [Fact]
    public void Two_stamps_side_by_side_are_allowed()
    {
        var other = MapJson("bank", 1, 1, FordMaterials, [0, 1, 0]);
        var def = Stamped(FordStamp, new RegionStamp("bank", new Tile(6, 6), 0));

        Assert.NotNull(ValidationTests.LoadChapter(World(def), Map("ford", FordMap), Map("bank", other)));
    }

    [Fact]
    public void A_recovery_spawn_inside_a_stamp_is_an_error()
        => Assert.Equal(
            [new ContentError(RegionFile, "/spawns/recovery", "The recovery spawn at (5, 6) is inside the stamp 'ford', so a beaten party would wake on its battlefield.")],
            ErrorsOf(Stamped(FordStamp) with { Spawns = new RegionSpawns(new Tile(0, 0), new Tile(5, 6)) }));

    [Fact]
    public void A_start_spawn_inside_a_stamp_is_an_error()
        => Assert.Equal(
            [new ContentError(RegionFile, "/spawns/start", "The start spawn at (4, 5) is inside the stamp 'ford', so a new campaign would begin on its battlefield.")],
            ErrorsOf(Stamped(FordStamp) with { Spawns = new RegionSpawns(new Tile(4, 5), new Tile(0, 0)) }));

    /// <summary>The stamp covers (4, 5) to (5, 6): a spawn on any side of it, touching it, is outside.</summary>
    [Theory]
    [InlineData(3, 5)]
    [InlineData(6, 5)]
    [InlineData(4, 4)]
    [InlineData(5, 7)]
    [InlineData(6, 7)]
    public void A_spawn_just_outside_a_stamp_loads(int x, int y)
        => Loads(Stamped(FordStamp) with { Spawns = new RegionSpawns(new Tile(x, y), new Tile(x, y)) });

    private static RegionDef WithEdges(string id, params RegionEdge[] edges) => Region(id) with { Edges = [.. edges] };

    [Theory]
    [InlineData("east", "west")]
    [InlineData("west", "east")]
    [InlineData("north", "south")]
    [InlineData("south", "north")]
    public void Edges_on_facing_sides_are_allowed(string side, string facing)
        => Loads(WithEdges("a", new RegionEdge("b", side, 12)), WithEdges("b", new RegionEdge("a", facing, -12)));

    [Fact]
    public void Edges_that_disagree_with_a_neighbour_are_an_error()
    {
        var errors = ValidationTests.ErrorsOfChapter(World(WithEdges("a", new RegionEdge("b", "east", 12))),
            World(WithEdges("b", new RegionEdge("a", "north", 12))));

        Assert.Equal(
            [
                new ContentError("chapter-01/world/a.json", "/edges/0", "This region meets 'b' on its east side, but 'b' has no west edge back to it."),
                new ContentError("chapter-01/world/b.json", "/edges/0", "This region meets 'a' on its north side, but 'a' has no south edge back to it."),
            ],
            errors);
    }

    [Theory]
    [InlineData(5, -5)]
    [InlineData(-5, 5)]
    [InlineData(0, 0)]
    [InlineData(int.MaxValue, -int.MaxValue)]
    public void Facing_edges_with_negated_offsets_are_allowed(int offset, int back)
        => Loads(WithEdges("a", new RegionEdge("b", "east", offset)), WithEdges("b", new RegionEdge("a", "west", back)));

    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, -4)]
    [InlineData(int.MinValue, int.MinValue)]    // minus this doesn't fit in an int, and wraps round to itself
    public void Facing_edges_whose_offsets_arent_negated_are_an_error(int offset, int back)
        => Assert.Equal(
            [
                new ContentError("chapter-01/world/a.json", "/edges/0",
                    $"This region meets 'b' on its east side at offset {offset}, so 'b' needs a west edge at offset {-(long)offset}, not {back}."),
                new ContentError("chapter-01/world/b.json", "/edges/0",
                    $"This region meets 'a' on its west side at offset {back}, so 'a' needs an east edge at offset {-(long)back}, not {offset}."),
            ],
            ValidationTests.ErrorsOfChapter(World(WithEdges("a", new RegionEdge("b", "east", offset))),
                World(WithEdges("b", new RegionEdge("a", "west", back)))));

    [Fact]
    public void A_neighbour_that_doesnt_list_this_region_disagrees()
        => Assert.Equal(
            [new ContentError("chapter-01/world/a.json", "/edges/0", "This region meets 'b' on its east side, but 'b' has no west edge back to it.")],
            ValidationTests.ErrorsOfChapter(World(WithEdges("a", new RegionEdge("b", "east", 12))), World(WithEdges("b"))));

    [Fact]
    public void A_region_with_no_neighbour_loaded_skips_its_edge_check()
        => Loads(WithEdges("a", new RegionEdge("skarrow_hills", "east", 12)));

    [Fact]
    public void A_neighbour_that_failed_to_load_reports_only_its_own_error()
    {
        var broken = WithEdges("b") with { Chapter = 0 };

        var errors = ValidationTests.ErrorsOfChapter(World(WithEdges("a", new RegionEdge("b", "east", 12))), World(broken));

        Assert.Equal("chapter-01/world/b.json", Assert.Single(errors).File);
    }

    [Fact]
    public void An_unknown_prop_is_an_error()
    {
        var def = Region(FordTiles) with
        {
            Props = [new RegionProps("nonexistent_prop", null, [new Tile(2, 2)])]
        };
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/props/0/id" && e.Message.Contains("Unknown prop 'nonexistent_prop'"));
    }

    [Fact]
    public void An_unknown_variant_is_an_error()
    {
        var def = Region(FordTiles) with
        {
            Props = [new RegionProps("tree", "rainbow", [new Tile(2, 2)])]
        };
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/props/0/variant" && e.Message.Contains("Unknown variant 'rainbow'"));
    }

    [Fact]
    public void A_prop_outside_the_region_is_an_error()
    {
        var def = Region(FordTiles) with
        {
            Props = [new RegionProps("tree", "autumn", [new Tile(35, 10)])]
        };
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/props/0/at/0" && e.Message.Contains("outside the 32x32 region"));
    }

    [Fact]
    public void A_prop_in_a_void_chunk_is_an_error()
    {
        var def = new RegionDef("r", "r", 1, [64, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(new Tile(0, 0), new Tile(1, 0)), [],
            [new RegionProps("tree", "autumn", [new Tile(40, 10)])]);
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/props/0/at/0" && e.Message.Contains("in a void chunk"));
    }

    [Fact]
    public void A_prop_on_a_stamp_is_an_error()
    {
        var def = Stamped(FordStamp) with
        {
            Props = [new RegionProps("tree", "autumn", [new Tile(4, 5)])]
        };
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/props/0/at/0" && e.Message.Contains("on the stamp 'ford'"));
    }

    [Fact]
    public void A_beat_walled_off_by_props_is_an_error()
    {
        var props = Enumerable.Range(0, 32).Select(y => new Tile(8, y)).ToArray();
        var def = Region(FordTiles) with
        {
            Beats = [new BeatSpot("ch1_beat", new Tile(10, 0))],
            Props = [new RegionProps("tree", "autumn", [.. props])]
        };
        var errors = ErrorsOf(def);
        Assert.Contains(errors, e => e.Pointer == "/beats/0/at" && e.Message.Contains("can't be reached from the start"));
    }

    [Fact]
    public void Region_props_stay_out_of_the_battle_hash()
    {
        var withoutProps = Region(FordTiles);
        var withProps = Region(FordTiles) with
        {
            Props = [new RegionProps("tree", "autumn", [new Tile(2, 2)])]
        };
        var pack1 = ValidationTests.LoadChapter(World(withoutProps), Map("ford", FordMap));
        var pack2 = ValidationTests.LoadChapter(World(withProps), Map("ford", FordMap));
        Assert.Equal(pack1.Hash, pack2.Hash);
    }
}
