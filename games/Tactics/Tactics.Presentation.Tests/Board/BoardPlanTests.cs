using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BoardPlan"/>: what is drawn for a map, in what order, on hand-made maps and the five real ones.</summary>
public sealed class BoardPlanTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones
        = ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    /// <summary>A map from rows of "height:material:flags" cells, material indices into <paramref name="materials"/>.</summary>
    private static BattleMap Map(string[] materials, ImmutableArray<PropPlacement> props, params string[] rows)
    {
        var cells = rows.Select(r => r.Split(' ')).ToArray();
        var bytes = cells.SelectMany(row => row.SelectMany(cell => cell.Split(':').Select(byte.Parse))).ToArray();
        return BattleMap.FromBytes("test", cells[0].Length, cells.Length, bytes, [.. materials], props, NoZones);
    }

    private static BoardPlan Plan(BattleMap map, BoardChanges? changes = null)
        => BoardPlan.Build(new BoardTerrain(map, changes), Pack.Art);

    private static IEnumerable<Face> Faces(BoardPlan plan) => plan.Items.OfType<Face>();

    private static IEnumerable<Face> Tops(BoardPlan plan) => Faces(plan).Where(f => f.Kind == FaceKind.Top);

    private static Face TopOf(BoardPlan plan, Tile tile) => Assert.Single(Tops(plan), f => f.Tile == tile);

    private static IEnumerable<Face> SidesOf(BoardPlan plan, Tile tile) => Faces(plan).Where(f => f.Kind == FaceKind.Side && f.Tile == tile);

    [Fact]
    public void Items_run_back_to_front_so_nearer_tiles_cover_farther_ones()
    {
        var plan = Plan(Pack.Map("river_crossing"));

        Assert.Equal(plan.Items.Select(i => i.Order).Order(), plan.Items.Select(i => i.Order));
        Assert.True(BoardOrder.Key(new Tile(0, 0), BoardLayer.Over) < BoardOrder.Key(new Tile(1, 0), BoardLayer.Under));
        Assert.True(BoardOrder.Key(new Tile(3, 2), BoardLayer.Over) < BoardOrder.Key(new Tile(3, 3), BoardLayer.Under));
    }

    [Fact]
    public void On_one_tile_the_ground_comes_first_and_units_stand_between_props_and_overlays()
    {
        var t = new Tile(4, 4);
        var layers = new[] { BoardLayer.Under, BoardLayer.Side, BoardLayer.Top, BoardLayer.Ground, BoardLayer.Prop, BoardLayer.Unit, BoardLayer.Over };

        Assert.Equal(layers.Select(l => BoardOrder.Key(t, l)).Order(), layers.Select(l => BoardOrder.Key(t, l)));
    }

    [Fact]
    public void A_top_samples_its_texture_in_world_space_one_texture_over_four_tiles()
    {
        var plan = Plan(Map(["grass/lush"], [], "0:0:0 0:0:0 0:0:0 0:0:0 0:0:0 0:0:0"));

        Assert.Equal([new Vector2(0.25f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.25f), new Vector2(0.25f, 0.25f)],
            TopOf(plan, new Tile(1, 0)).Uvs);
        Assert.Equal(0.25f, TopOf(plan, new Tile(5, 0)).Uvs[0].X);   // 5 tiles in wraps to the same strip of the texture
        Assert.EndsWith("grass_lush.png", TopOf(plan, new Tile(1, 0)).Image!.Path);
    }

    [Fact]
    public void A_top_stands_at_its_height()
    {
        var plan = Plan(Map(["rock/grey"], [], "3:0:0"));

        Assert.Equal(BoardProjection.Corner(0, 0, 3), TopOf(plan, new Tile(0, 0)).Points[0]);
    }

    [Fact]
    public void A_step_down_shows_a_side_as_tall_as_the_step_and_a_level_neighbour_shows_none()
    {
        var plan = Plan(Map(["rock/grey"], [], "2:0:0 0:0:0 0:0:0"));
        var side = Assert.Single(SidesOf(plan, new Tile(0, 0)), s => s.Facing == Facing.U);

        Assert.Equal(BoardProjection.Corner(1, 0, 2), side.Points[0]);
        Assert.Equal(BoardProjection.Corner(1, 0, 0), side.Points[3]);
        Assert.Equal(1f, side.Uvs[3].Y);   // 2 steps are 32 px, one tile of the side texture
        Assert.DoesNotContain(SidesOf(plan, new Tile(1, 0)), s => s.Facing == Facing.U);
    }

    [Fact]
    public void Only_the_two_faces_toward_the_viewer_are_drawn()
    {
        var plan = Plan(Map(["rock/grey"], [], "0:0:0 0:0:0 0:0:0", "0:0:0 2:0:0 0:0:0", "0:0:0 0:0:0 0:0:0"));

        Assert.Equal([Facing.U, Facing.V], SidesOf(plan, new Tile(1, 1)).Select(s => s.Facing).Order());
        Assert.Empty(SidesOf(plan, new Tile(1, 0)));   // behind the raised tile: its faces look away
    }

    [Fact]
    public void Map_edges_get_a_skirt_down_to_the_board_base()
    {
        var plan = Plan(Map(["rock/grey"], [], "1:0:0 0:0:0"));
        var skirt = Assert.Single(SidesOf(plan, new Tile(1, 0)), s => s.Facing == Facing.U);

        Assert.Equal(BoardProjection.Corner(2, 1, -1), skirt.Points[2]);
    }

    [Fact]
    public void A_lowered_tile_does_not_deepen_the_skirt_around_the_whole_board()
    {
        var map = Map(["rock/grey"], [], "1:0:0 1:0:0");

        Assert.Equal(new BoardTerrain(map, null).Base, new BoardTerrain(map, new BoardChanges([], [new HeightChange(new Tile(0, 0), -1)], [])).Base);
    }

    [Fact]
    public void A_lip_lies_over_the_top_of_a_side_under_a_top_that_has_one()
    {
        var plan = Plan(Map(["grass/lush"], [], "2:0:0 0:0:0"));
        var lip = Assert.Single(Faces(plan), f => f.Kind == FaceKind.Lip && f.Tile == new Tile(0, 0) && f.Facing == Facing.U);
        var side = Assert.Single(SidesOf(plan, new Tile(0, 0)), s => s.Facing == Facing.U);

        Assert.Equal(side.Points[0], lip.Points[0]);
        Assert.Equal(12f, lip.Points[3].Y - lip.Points[0].Y);   // the lip texture is 12 px tall
        Assert.True(plan.Items.IndexOf(lip) > plan.Items.IndexOf(side));
        Assert.EndsWith("grass_lip_lush.png", lip.Image!.Path);
    }

    [Fact]
    public void A_void_sinks_well_below_its_height_so_the_chasm_walls_show()
    {
        var plan = Plan(Map(["cave_floor/cool", "void/deep"], [], "2:0:0 0:1:16 2:0:0"));
        var wall = Assert.Single(SidesOf(plan, new Tile(0, 0)), s => s.Facing == Facing.U);

        Assert.Equal(BoardProjection.Corner(1, 0, -2), TopOf(plan, new Tile(1, 0)).Points[0]);
        Assert.EndsWith("chasm_side_cool.png", wall.Image!.Path);
        Assert.Equal(1f, wall.Uvs[3].Y);          // 4 steps: the whole 2-tile chasm wall
        Assert.Equal(BoardProjection.Corner(1, 0, -2), wall.Points[3]);
    }

    [Fact]
    public void A_wall_stands_taller_than_its_height_and_has_no_grid_on_it()
    {
        var plan = Plan(Map(["dungeon_wall/dark", "dungeon_floor/dark"], [], "2:0:4 0:1:0"));
        var wall = TopOf(plan, new Tile(0, 0));

        Assert.Equal(BoardProjection.Corner(0, 0, 2 + BoardTerrain.WallRise), wall.Points[0]);
        Assert.False(wall.Grid);
        Assert.True(TopOf(plan, new Tile(1, 0)).Grid);
    }

    [Fact]
    public void Deep_water_lies_below_its_banks_and_a_ford_is_visibly_shallower()
    {
        var plan = Plan(Map(["grass/lush", "water/clear"], [], "0:0:0 0:1:17 0:1:3 0:0:0"));

        Assert.True(TopOf(plan, new Tile(1, 0)).Points[0].Y > BoardProjection.Corner(1, 0, 0).Y);
        Assert.True(TopOf(plan, new Tile(2, 0)).Shade > TopOf(plan, new Tile(1, 0)).Shade);
    }

    [Fact]
    public void A_bridge_is_a_thin_deck_with_the_chasm_visible_under_it()
    {
        var plan = Plan(Map(["grass/lush", "void/deep", "mine_boards/oak"], [],
            "2:0:0 0:1:16 2:0:0",
            "2:0:0 2:2:0 2:0:0",
            "2:0:0 0:1:16 2:0:0"));
        var deck = new Tile(1, 1);
        var side = Assert.Single(SidesOf(plan, deck), s => s.Facing == Facing.V);

        Assert.Equal(BoardProjection.Corner(2, 2, 2 - BoardTerrain.DeckDepth), side.Points[2]);
        var under = Assert.Single(Faces(plan), f => f.Kind == FaceKind.Under && f.Tile == deck);
        Assert.Equal(BoardProjection.Corner(1, 1, -2), under.Points[0]);
        Assert.EndsWith("void_abyss.png", under.Image!.Path);
    }

    [Fact]
    public void A_bank_beside_a_bridge_keeps_its_chasm_wall_under_the_deck()
    {
        var plan = Plan(Map(["grass/lush", "void/deep", "mine_boards/oak"], [], "2:0:0 2:2:0 2:0:0", "2:0:0 0:1:16 2:0:0"));
        var wall = Assert.Single(SidesOf(plan, new Tile(0, 0)), s => s.Facing == Facing.U);

        Assert.Equal(BoardProjection.Corner(1, 0, -2), wall.Points[3]);
    }

    [Fact]
    public void Boards_laid_on_solid_ground_are_not_a_bridge()
    {
        var plan = Plan(Map(["cave_floor/cool", "mine_boards/oak"], [], "2:0:0 2:1:0 2:0:0"));

        Assert.DoesNotContain(Faces(plan), f => f.Kind == FaceKind.Under);
    }

    [Fact]
    public void Height_changes_move_a_tile()
    {
        var changes = new BoardChanges([], [new HeightChange(new Tile(1, 0), 2)], []);
        var plan = Plan(Map(["rock/grey"], [], "0:0:0 0:0:0 0:0:0"), changes);

        Assert.Equal(BoardProjection.Corner(1, 0, 2), TopOf(plan, new Tile(1, 0)).Points[0]);
    }

    [Fact]
    public void A_sunk_tile_turns_into_the_maps_own_chasm()
    {
        var map = Map(["grass/lush", "void/deep", "mine_boards/oak"], [], "2:0:0 2:2:0 0:1:16");
        var changes = new BoardChanges([], [new HeightChange(new Tile(1, 0), -2)], [new Tile(1, 0)]);
        var plan = Plan(map, changes);

        Assert.EndsWith("void_abyss.png", TopOf(plan, new Tile(1, 0)).Image!.Path);
        Assert.Equal(TopOf(plan, new Tile(2, 0)).Points[0].Y - 16, TopOf(plan, new Tile(1, 0)).Points[0].Y);   // level with it
    }

    [Fact]
    public void On_a_map_with_no_deep_tiles_a_sunk_tile_is_still_a_void()
        => Assert.EndsWith("void_abyss.png", TopOf(Plan(Map(["grass/lush"], [], "1:0:0 1:0:0"),
            new BoardChanges([], [], [new Tile(1, 0)])), new Tile(1, 0)).Image!.Path);

    [Fact]
    public void A_prop_stands_with_its_anchor_on_the_tile_centre()
    {
        var at = new Tile(1, 0);
        var plan = Plan(Map(["cobble/grey"], [new PropPlacement("crate", at, true, Cover.Half)], "1:0:0 1:0:0"));
        var sprite = Assert.Single(plan.Items.OfType<Sprite>());

        Assert.Equal(BoardProjection.Centre(at, 1) - new Vector2(13, 27), sprite.TopLeft);
        Assert.Equal(new Vector2(26, 28), sprite.Size);
        Assert.Equal(BoardOrder.Key(at, BoardLayer.Prop), sprite.Order);
    }

    [Fact]
    public void A_prop_picks_up_the_look_of_the_ground_it_stands_on()
    {
        var map = Map(["dungeon_floor/dark"], [new PropPlacement("sarcophagus", new Tile(0, 0), true, Cover.Half)], "0:0:0");

        Assert.EndsWith("sarcophagus_dark.png", Assert.Single(Plan(map).Items.OfType<Sprite>()).Image.Path);
    }

    [Fact]
    public void A_prop_with_no_art_is_left_out_not_a_crash()
        => Assert.Empty(Plan(Map(["grass/lush"], [new PropPlacement("nope", new Tile(0, 0), true, Cover.Half)], "0:0:0")).Items.OfType<Sprite>());

    [Fact]
    public void A_material_with_no_art_draws_as_a_flat_colour()
    {
        var top = TopOf(Plan(Map(["no_such/stuff"], [], "1:0:0")), new Tile(0, 0));

        Assert.Null(top.Image);
        Assert.NotNull(top.Fill);
    }

    [Fact]
    public void A_zone_lays_its_hazard_on_each_tile_top_with_its_flames_above_the_units()
    {
        var at = new Tile(1, 0);
        var changes = new BoardChanges([new ZoneView(1, "fire", [at, new Tile(0, 0)])], [], []);
        var plan = Plan(Map(["cobble/grey"], [], "1:0:0 1:0:0"), changes);
        var sprites = plan.Items.OfType<Sprite>().Where(s => s.Tile == at).ToArray();

        Assert.Equal(2, sprites.Length);
        Assert.Equal(BoardOrder.Key(at, BoardLayer.Ground), sprites[0].Order);
        Assert.Equal(BoardProjection.Centre(at, 1) - new Vector2(32, 16), sprites[0].TopLeft);
        Assert.EndsWith("burning.png", sprites[0].Image.Path);
        Assert.Equal(BoardOrder.Key(at, BoardLayer.Over), sprites[1].Order);
        Assert.Equal(BoardProjection.Centre(at, 1) - new Vector2(32, 32), sprites[1].TopLeft);
        Assert.EndsWith("burning_over.png", sprites[1].Image.Path);
    }

    [Theory]
    [InlineData("fire", "burning")]
    [InlineData("web", "web")]
    [InlineData("ice", "ice")]
    [InlineData("grease", "oil")]
    [InlineData("holy", "consecrated")]
    [InlineData("smoke", "darkness")]
    [InlineData("entangle", "thorns")]
    [InlineData("spikes", "spikes")]
    public void Zone_kinds_map_onto_the_hazard_set(string kind, string hazard)
        => Assert.Equal(hazard, ZoneArt.HazardFor(kind));

    [Fact]
    public void A_zone_kind_with_no_hazard_art_still_shows_as_a_tint()
    {
        var at = new Tile(0, 0);
        var plan = Plan(Map(["cobble/grey"], [], "1:0:0"), new BoardChanges([new ZoneView(1, "grand_sigil", [at])], [], []));
        var tint = Assert.Single(Faces(plan), f => f.Kind == FaceKind.Tint);

        Assert.Null(tint.Image);
        Assert.Equal(BoardOrder.Key(at, BoardLayer.Ground), tint.Order);
        Assert.True(tint.Alpha < 1);
    }

    [Fact]
    public void A_zone_or_change_outside_the_map_is_ignored()
    {
        var off = new Tile(9, 9);
        var plan = Plan(Map(["cobble/grey"], [], "1:0:0"),
            new BoardChanges([new ZoneView(1, "fire", [off])], [new HeightChange(off, 2)], [off]));

        Assert.Empty(plan.Items.OfType<Sprite>());
    }

    [Theory]
    [InlineData("river_crossing")]
    [InlineData("forest_ruins")]
    [InlineData("crypt_hall")]
    [InlineData("warlord_bridge")]
    [InlineData("crystal_cave")]
    public void Every_sample_map_draws_with_real_art_and_fits_at_twice_size(string id)
    {
        var map = Pack.Map(id);
        var plan = Plan(map);

        Assert.Equal(map.Width * map.Height, Tops(plan).Count());
        Assert.All(Faces(plan), f => Assert.NotNull(f.Image));
        Assert.Equal(map.Props.Length, plan.Items.OfType<Sprite>().Count());
        Assert.All(plan.Items.OfType<Sprite>(), s => Assert.True(File.Exists(Path.Combine(Pack.Root, s.Image.Path))));
        Assert.Equal(2, BoardFit.For(new BoardTerrain(map, null), Pack.Art).Scale);
    }

    [Theory]
    [InlineData("river_crossing", "water")]
    [InlineData("forest_ruins", "flagstone")]
    [InlineData("crypt_hall", "crypt_tiles")]
    [InlineData("warlord_bridge", "mine_boards")]
    [InlineData("crystal_cave", "wet_stone")]
    public void Each_maps_signature_ground_is_drawn(string id, string texture)
        => Assert.Contains(Tops(Plan(Pack.Map(id))), f => f.Image!.Path.Contains(texture, StringComparison.Ordinal));

    [Fact]
    public void The_plan_says_whether_it_needs_a_clock()
    {
        Assert.True(Plan(Pack.Map("river_crossing")).Animated);    // water
        Assert.False(Plan(Map(["cobble/grey"], [], "1:0:0")).Animated);
    }

    [Fact]
    public void Bounds_cover_every_point_drawn()
    {
        var plan = Plan(Pack.Map("warlord_bridge"));

        foreach (var point in Faces(plan).SelectMany(f => f.Points))
        {
            Assert.InRange(point.X, plan.Min.X, plan.Max.X);
            Assert.InRange(point.Y, plan.Min.Y, plan.Max.Y);
        }
    }
}
