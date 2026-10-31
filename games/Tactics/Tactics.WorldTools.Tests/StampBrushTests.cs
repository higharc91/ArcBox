using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class StampBrushTests
{
    private const string Grass = "grass/lush", River = "water/river";

    /// <summary>A 2x2 map: heights 1, 1, 2, 1, with shallow water at (0, 1).</summary>
    private static BattleMap Ford() => BattleMap.FromBytes("ford", 2, 2, (byte[])[1, 1, 0, 1, 1, 0, 2, 0, 1, 1, 1, 0],
        [River, Grass], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);

    [Fact]
    public void A_stroke_paints_the_map_at_the_marker_with_its_height_offset()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 1, null, 32, 32);

        Assert.Empty(stroke.Problems);
        Assert.Contains(new StampCell(new Tile(4, 6), River, 3, TileFlags.Water), stroke.Cells);
        Assert.Contains(new StampCell(new Tile(5, 5), Grass, 2, TileFlags.None), stroke.Cells);
        Assert.Equal(4, stroke.Cells.Count);
    }

    [Fact]
    public void A_stroke_matches_what_a_stamped_source_holds()
    {
        var source = new RegionSource("r", "R", 1, 32, 32);
        source.Stamp(Ford(), new Tile(4, 5), 1);

        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 1, null, 32, 32);

        Assert.All(stroke.Cells, c => Assert.Equal(new TileInfoNamed(c.Height, c.Material, c.Flags), source.Get(c.At)));
    }

    [Fact]
    public void A_stroke_that_doesnt_fit_is_refused()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(31, 5), 0, null, 32, 32);

        Assert.Empty(stroke.Cells);
        Assert.Equal(["The stamp 'ford' at (31, 5) is 2x2, which doesn't fit in the 32x32 region. Move the marker."], stroke.Problems);
    }

    [Fact]
    public void A_stroke_that_pushes_a_height_past_9_is_refused()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 8, null, 32, 32);

        Assert.Empty(stroke.Cells);
        Assert.Equal(["The stamp 'ford' at height 8 would put a tile at 10, but heights go from 0 to 9. Change HeightOffset."], stroke.Problems);
    }

    [Fact]
    public void Moving_a_stamp_lists_the_old_tiles_it_no_longer_covers()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(5, 5), 0, new Tile(4, 5), 32, 32);

        Assert.Equal([new Tile(4, 5), new Tile(4, 6)], stroke.LeftBehind);
    }

    [Fact]
    public void Stamping_in_place_leaves_nothing_behind()
        => Assert.Empty(StampBrush.Apply(Ford(), new Tile(4, 5), 0, new Tile(4, 5), 32, 32).LeftBehind);

    [Fact]
    public void A_first_stamp_leaves_nothing_behind()
        => Assert.Equal("Stamped 'ford' at (4, 5). Undo takes it back.", StampBrush.Apply(Ford(), new Tile(4, 5), 0, null, 32, 32).Report);

    [Fact]
    public void A_move_asks_the_painter_to_repaint_what_it_left()
        => Assert.Equal(
            "Stamped 'ford' at (5, 5). 2 tiles, x 4 to 4, y 5 to 6, first (4, 5), still hold the old stamp's tiles: repaint them. Undo takes it all back.",
            StampBrush.Apply(Ford(), new Tile(5, 5), 0, new Tile(4, 5), 32, 32).Report);

    [Fact]
    public void A_map_is_read_from_its_pack_file()
    {
        var map = StampMaps.Read(ContentPaths.BaseRules, "river_crossing")!;
        var real = RulesPack.Load([ContentPaths.BaseRules]).MapFor("river_crossing")!;

        Assert.Equal((real.Width, real.Height), (map.Width, map.Height));
        Assert.Equal(real.Materials, map.Materials);
        Assert.Equal(real[new Tile(3, 2)], map[new Tile(3, 2)]);
    }

    [Fact]
    public void A_map_that_isnt_there_is_null()
        => Assert.Null(StampMaps.Read(ContentPaths.BaseRules, "nowhere"));

    [Fact]
    public void A_map_id_that_isnt_a_file_name_is_null()
        => Assert.Null(StampMaps.Read(ContentPaths.BaseRules, "../manifest"));
}
