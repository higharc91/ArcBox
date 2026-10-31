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

    private static BattleMap? Maps(string id) => Ford();

    [Fact]
    public void A_stroke_paints_the_map_at_the_marker_with_its_height_offset()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 1, null, [], Maps, 32, 32);

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

        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 1, null, [], Maps, 32, 32);

        Assert.All(stroke.Cells, c => Assert.Equal(new TileInfoNamed(c.Height, c.Material, c.Flags), source.Get(c.At)));
    }

    [Fact]
    public void A_stroke_that_doesnt_fit_is_refused()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(31, 5), 0, null, [], Maps, 32, 32);

        Assert.Empty(stroke.Cells);
        Assert.Equal(["The stamp 'ford' at (31, 5) is 2x2, which doesn't fit in the 32x32 region. Move the marker."], stroke.Problems);
    }

    [Fact]
    public void A_stroke_that_pushes_a_height_past_9_is_refused()
    {
        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 8, null, [], Maps, 32, 32);

        Assert.Empty(stroke.Cells);
        Assert.Equal(["The stamp 'ford' at height 8 would put a tile at 10, but heights go from 0 to 9. Change HeightOffset."], stroke.Problems);
    }

    private static readonly RegionStamp At45 = new("ford", new Tile(4, 5), 0), At95 = new("ford", new Tile(9, 5), 0);

    private static StampStroke Stamp(Tile at, RegionStamp? applied, params RegionStamp[] pending)
        => StampBrush.Apply(Ford(), at, 0, applied, pending, Maps, 32, 32);

    [Fact]
    public void Moving_a_stamp_keeps_its_old_area_pending()
        => Assert.Equal([At45], Stamp(new Tile(9, 5), At45).Pending);

    [Fact]
    public void A_partly_overlapping_move_keeps_the_old_area_pending()
        => Assert.Equal([At45], Stamp(new Tile(5, 5), At45).Pending);

    [Fact]
    public void A_second_move_keeps_both_old_areas()
        => Assert.Equal([At45, At95], Stamp(new Tile(14, 5), At95, At45).Pending);

    [Fact]
    public void Moving_back_clears_the_area_it_covers_again()
        => Assert.Equal([At95], Stamp(new Tile(4, 5), At95, At45).Pending);

    [Fact]
    public void Stamping_in_place_leaves_nothing_pending()
        => Assert.Empty(Stamp(new Tile(4, 5), At45).Pending);

    [Fact]
    public void A_new_height_in_place_leaves_nothing_pending()
        => Assert.Empty(Stamp(new Tile(4, 5), At45 with { Height = 3 }).Pending);

    [Fact]
    public void A_bigger_old_map_keeps_what_the_new_one_doesnt_cover()
    {
        var big = BattleMap.FromBytes("big", 3, 1, new byte[9], [Grass], [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
        var old = new RegionStamp("big", new Tile(4, 5), 0);

        var stroke = StampBrush.Apply(Ford(), new Tile(4, 5), 0, old, [], id => id == "big" ? big : Ford(), 32, 32);

        Assert.Equal([old], stroke.Pending);
    }

    [Fact]
    public void A_refused_stroke_changes_nothing_pending()
        => Assert.Equal([At95], Stamp(new Tile(31, 5), At45, At95).Pending);

    [Fact]
    public void A_first_stamp_can_be_undone()
        => Assert.Equal("Stamped 'ford' at (4, 5). Undo takes it back.", Stamp(new Tile(4, 5), null).Report);

    [Fact]
    public void A_move_says_how_many_old_areas_are_pending()
        => Assert.Equal("Stamped 'ford' at (14, 5). 2 old areas still hold a stamp: repaint them. Undo takes this one back.", Stamp(new Tile(14, 5), At95, At45).Report);

    [Fact]
    public void One_old_area_is_singular()
        => Assert.Equal("Stamped 'ford' at (9, 5). 1 old area still holds a stamp: repaint it. Undo takes this one back.", Stamp(new Tile(9, 5), At45).Report);

    [Fact]
    public void A_pending_area_says_which_tiles_are_left()
        => Assert.Equal("2 tiles, x 4 to 4, y 5 to 6, first (4, 5), still hold the old stamp 'ford'. Repaint them, then press Done repainting.",
            StampBrush.PendingWarning(At45, Ford(), new HashSet<Tile> { new(5, 5), new(5, 6) }));

    [Fact]
    public void A_pending_area_wholly_covered_has_no_warning()
        => Assert.Null(StampBrush.PendingWarning(At45, Ford(), new HashSet<Tile> { new(4, 5), new(5, 5), new(4, 6), new(5, 6) }));

    [Fact]
    public void A_pending_area_of_a_lost_map_still_warns()
        => Assert.Equal("The old stamp 'ford' at (4, 5) may still be in the world. Repaint it, then press Done repainting.",
            StampBrush.PendingWarning(At45, null, new HashSet<Tile>()));

    [Fact]
    public void A_stamp_record_survives_being_stored_on_a_marker()
        => Assert.Equal(new RegionStamp("river_crossing", new Tile(-4, 15), 2), StampBrush.Decode(StampBrush.Encode(new RegionStamp("river_crossing", new Tile(-4, 15), 2))));

    [Theory]
    [InlineData("")]
    [InlineData("ford|1|2")]
    [InlineData("ford|a|2|3")]
    public void A_bad_stamp_record_is_null(string text)
        => Assert.Null(StampBrush.Decode(text));

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
