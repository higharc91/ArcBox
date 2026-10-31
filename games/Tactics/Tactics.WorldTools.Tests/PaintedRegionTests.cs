using Tactics.Logic;

namespace Tactics.WorldTools.Tests;

public sealed class PaintedRegionTests
{
    private const string Grass = "grass/lush";

    private static readonly Tile A = new(1, 2), B = new(3, 4);

    /// <summary>An 8x8 region with every tile painted, so a test's only problem is the one it sets up.</summary>
    private static PaintedRegion Painted() => new("r", "Region", 1, 8, 8)
    {
        Materials = Grid(8, 8).ToDictionary(t => t, t => t == B ? "water/clear" : Grass),
        Starts = [A],
        Recoveries = [B],
    };

    private static IEnumerable<Tile> Grid(int width, int height)
        => Enumerable.Range(0, width * height).Select(i => new Tile(i % width, i / width));

    private static Dictionary<Tile, string> Without(PaintedRegion painted, params Tile[] tiles)
        => painted.Materials.Where(m => !tiles.Contains(m.Key)).ToDictionary();

    [Fact]
    public void The_three_layers_become_one_tile()
    {
        var painted = Painted() with
        {
            Heights = new Dictionary<Tile, int> { [A] = 3 },
            Flags = new Dictionary<Tile, TileFlags> { [A] = TileFlags.Water | TileFlags.Deep },
        };

        var (source, problems) = painted.ToSource();

        Assert.Empty(problems);
        Assert.Equal(new TileInfoNamed(3, Grass, TileFlags.Water | TileFlags.Deep), source.Get(A));
    }

    [Fact]
    public void A_tile_with_no_height_or_flags_is_plain_ground_at_0()
        => Assert.Equal(new TileInfoNamed(0, "water/clear", TileFlags.None), Painted().ToSource().Source.Get(B));

    [Fact]
    public void A_region_keeps_its_names_size_and_markers()
    {
        var painted = Painted() with
        {
            Beats = [new BeatSpot("ch1_old_maud", B)],
            Stamps = [new RegionStamp("ford", A, 2)],
        };

        var source = painted.ToSource().Source;

        Assert.Equal(("r", "Region", 1, 8, 8), (source.Id, source.Name, source.Chapter, source.Width, source.Height));
        Assert.Equal((A, B), (source.Start, source.Recovery));
        Assert.Equal([new BeatSpot("ch1_old_maud", B)], source.Beats);
        Assert.Equal([new RegionStamp("ford", A, 2)], source.Stamps);
    }

    [Fact]
    public void A_stamp_keeps_the_painted_tiles_under_it()
    {
        // If a stamp copied its map here, a tile painted over would be put back and the export could never see the change.
        var source = (Painted() with { Stamps = [new RegionStamp("ford", A, 0)] }).ToSource().Source;

        Assert.Equal(Grass, source.Get(A)!.Material);
        Assert.Equal(64, source.Tiles.Count);
    }

    [Fact]
    public void One_height_with_no_material_names_the_tile()
    {
        var painted = Painted();
        painted = painted with { Materials = Without(painted, new Tile(5, 5)), Heights = new Dictionary<Tile, int> { [new Tile(5, 5)] = 1 } };

        Assert.Contains("The tile (5, 5) has a height but no material. Paint the Material layer there, or erase the Height.", painted.ToSource().Problems);
    }

    [Fact]
    public void Many_heights_with_no_material_are_counted_with_their_box()
    {
        var bare = Enumerable.Range(0, 30).ToDictionary(i => new Tile(10 + i % 6, 2 + i / 6), _ => 1);
        var painted = Painted() with { Heights = bare };

        Assert.Equal(["30 tiles, x 10 to 15, y 2 to 6, first (10, 2), have a height but no material. Paint the Material layer there, or erase the Height."],
            painted.ToSource().Problems);
    }

    [Fact]
    public void Flags_with_no_material_are_a_problem()
    {
        var painted = Painted() with { Flags = new Dictionary<Tile, TileFlags> { [new Tile(9, 5)] = TileFlags.Water } };

        Assert.Equal(["The tile (9, 5) has flags but no material. Paint the Material layer there, or erase the Flags."], painted.ToSource().Problems);
    }

    [Fact]
    public void A_scene_with_no_start_says_what_to_add()
        => Assert.Equal(["The scene has no start spawn. Add a SpawnMarker under Spawns and set Kind to Start."],
            (Painted() with { Starts = [] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_no_recovery_says_what_to_add()
        => Assert.Equal(["The scene has no recovery spawn. Add a SpawnMarker under Spawns and set Kind to Recovery."],
            (Painted() with { Recoveries = [] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_two_starts_is_a_problem()
        => Assert.Equal(["The scene has 2 start spawns, at (1, 2) and (3, 4), but needs one. Delete the extra."],
            (Painted() with { Starts = [A, B] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_three_recoveries_is_a_problem()
        => Assert.Equal(["The scene has 3 recovery spawns, at (1, 2), (3, 4) and (1, 2), but needs one. Delete the extras."],
            (Painted() with { Recoveries = [A, B, A] }).ToSource().Problems);

    [Fact]
    public void A_hole_in_a_painted_chunk_is_a_problem_with_its_count_and_box()
    {
        var painted = Painted();

        Assert.Equal(
            ["2 tiles, x 5 to 6, y 1 to 1, first (5, 1), have no material in a painted chunk, which plays as an invisible wall. Paint them, and add the Impassable flag if a wall is what you want."],
            (painted with { Materials = Without(painted, new Tile(5, 1), new Tile(6, 1)) }).ToSource().Problems);
    }

    [Fact]
    public void One_hole_names_its_tile()
    {
        var painted = Painted();

        Assert.Equal(
            ["The tile (5, 1) has no material in a painted chunk, which plays as an invisible wall. Paint it, and add the Impassable flag if a wall is what you want."],
            (painted with { Materials = Without(painted, new Tile(5, 1)) }).ToSource().Problems);
    }

    [Fact]
    public void A_whole_missing_chunk_is_void_not_a_hole()
    {
        var painted = Painted() with { Width = 64, Height = 8 };

        // Chunk (0, 0) needs painting up to row 7 only, as the region ends there, but it has 32 columns.
        var full = Grid(32, 8).ToDictionary(t => t, _ => Grass);

        Assert.Empty((painted with { Materials = full }).ToSource().Problems);
    }

    [Fact]
    public void Tiles_past_the_regions_edge_in_a_chunk_arent_holes()
        => Assert.Empty(Painted().ToSource().Problems);

    [Fact]
    public void A_beat_with_no_id_is_a_problem()
        => Assert.Equal(["The beat marker at (3, 4) has no Beat id. Set its Beat, or delete it."],
            (Painted() with { Beats = [new BeatSpot("", B)] }).ToSource().Problems);

    [Fact]
    public void A_beat_marked_twice_is_a_problem()
        => Assert.Equal(["The beat 'ch1_x' has 2 markers, at (1, 2) and (3, 4). Delete the extra."],
            (Painted() with { Beats = [new BeatSpot("ch1_x", A), new BeatSpot("ch1_x", B)] }).ToSource().Problems);

    [Fact]
    public void A_moved_node_is_a_problem()
        => Assert.Equal(["The Beats node has been moved, so its markers aren't on the tiles they show. Set its Position back to (0, 0)."],
            (Painted() with { MovedNodes = ["Beats"] }).ToSource().Problems);

    [Fact]
    public void A_marker_under_the_wrong_parent_is_a_problem()
        => Assert.Equal(["The StampMarker 'gate' at (3, 4) is under Beats, so it isn't exported. Move it under Stamps."],
            (Painted() with { Strays = [new StrayNode("StampMarker 'gate'", "Beats", "Move it under Stamps", B)] }).ToSource().Problems);

    [Fact]
    public void A_scene_file_that_isnt_named_after_its_region_is_a_problem()
        => Assert.Equal(["The scene is named 'other' but its region Id is 'r', and two scenes with one Id would overwrite each other's file. Rename the scene or change Id."],
            (Painted() with { SceneFile = "other" }).ToSource().Problems);

    [Fact]
    public void A_scene_file_named_after_its_region_is_fine()
        => Assert.Empty((Painted() with { SceneFile = "r" }).ToSource().Problems);

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(31.9, 32, 0, 1)]
    [InlineData(16, 48, 0, 1)]
    [InlineData(320, 63.5, 10, 1)]
    [InlineData(-0.5, -32, -1, -1)]
    [InlineData(-32.5, 0, -2, 0)]
    public void A_marker_stands_on_the_tile_its_position_falls_in(double x, double y, int tileX, int tileY)
        => Assert.Equal(new Tile(tileX, tileY), PaintedRegion.TileAt(x, y));
}
