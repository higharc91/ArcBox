using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="UnitPlan"/> and <see cref="BoardOrder.Merge"/>: how units go on the board.</summary>
public sealed class UnitPlanTests
{
    private const string Goblin = "characters.goblin_archer";

    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain River = new(Pack.Map("river_crossing"), null);
    private static readonly BoardFit Fit = BoardFit.For(River, Pack.Art);

    private static UnitView Unit(string id, string side, int x, int y, string? sprite = Goblin, int hp = 10, int maxHp = 10, string life = "Up",
        int countdown = 0, ImmutableArray<string> conditions = default)
        => new(new UnitId(id), id, side, sprite, new Tile(x, y), hp, maxHp, 0, life, countdown, conditions.IsDefault ? [] : conditions, null);

    private static ImmutableArray<BoardUnit> Plan(string? active, params UnitView[] units)
        => UnitPlan.Build(Battle(active, units), River, Art.Value, Fit);

    private static BattleView Battle(string? active, params UnitView[] units)
        => new("river_crossing", "river_crossing", 1, [], active is null ? null : new UnitId(active), [.. units], [], null);

    private static BoardUnit One(string? active, params UnitView[] units) => Assert.Single(Plan(active, units), u => u.Id.Value == units[0].Id.Value);

    [Fact]
    public void A_unit_stands_on_the_centre_of_its_tile_as_the_terrain_draws_it()
    {
        var tile = new Tile(12, 3);   // the ridge: raised

        var unit = One(null, Unit("a", "Enemy", tile.X, tile.Y));

        Assert.Equal(River.Centre(tile), unit.Feet);
        Assert.True(River.Top(tile) > 0);
    }

    [Fact]
    public void A_unit_in_deep_water_stands_where_the_water_is_drawn()
    {
        var map = Pack.Map("river_crossing");
        var deep = River.Tiles.First(River.Deep);

        Assert.True(map.InBounds(deep));
        Assert.Equal(River.Centre(deep), One(null, Unit("a", "Party", deep.X, deep.Y)).Feet);
    }

    [Fact]
    public void Units_sort_in_their_tiles_slot_and_are_returned_back_to_front()
    {
        var units = Plan(null, Unit("front", "Party", 5, 9), Unit("back", "Enemy", 2, 3));

        Assert.Equal(["back", "front"], units.Select(u => u.Id.Value));
        Assert.Equal(BoardOrder.Key(new Tile(5, 9), BoardLayer.Unit), units[1].Order);
    }

    [Fact]
    public void A_sprite_up_to_a_tile_wide_stays_in_its_tiles_slot()
    {
        var troll = One(null, Unit("a", "Enemy", 4, 4, "bosses.troll.troll"));

        Assert.Equal(BoardOrder.Key(new Tile(4, 4), BoardLayer.Unit), troll.Order);
    }

    [Fact]
    public void A_wider_sprite_goes_after_everything_on_its_diagonal_so_the_next_tile_along_cannot_cut_it()
    {
        var at = new Tile(6, 5);
        var dragon = One(null, Unit("a", "Enemy", at.X, at.Y, "bosses.dragon.red"));

        var neighbour = new Tile(at.X + 1, at.Y - 1);    // same diagonal, painted after the dragon's own tile
        Assert.True(dragon.Order > BoardOrder.Key(neighbour, BoardLayer.Prop));
        Assert.True(dragon.Order > BoardOrder.Key(new Tile(at.X + at.Y, 0), BoardLayer.Prop));
    }

    [Fact]
    public void A_wider_sprite_is_still_hidden_by_the_tiles_in_front_of_it_and_under_what_rises_above_units()
    {
        var at = new Tile(6, 5);
        var dragon = One(null, Unit("a", "Enemy", at.X, at.Y, "bosses.dragon.red"));

        Assert.True(dragon.Order < BoardOrder.Key(at with { X = at.X + 1 }, BoardLayer.Under));
        Assert.True(dragon.Order < BoardOrder.Key(at with { Y = at.Y + 1 }, BoardLayer.Under));
        Assert.True(dragon.Order < BoardOrder.Key(at, BoardLayer.Over));
    }

    [Fact]
    public void What_rises_above_units_goes_after_its_whole_diagonal()
    {
        var at = new Tile(6, 5);

        Assert.True(BoardOrder.Key(at, BoardLayer.Over) > BoardOrder.Key(new Tile(9, 2), BoardLayer.Unit));
        Assert.True(BoardOrder.Key(at, BoardLayer.Over) < BoardOrder.Key(at with { X = 7 }, BoardLayer.Under));
    }

    [Fact]
    public void A_tall_tile_in_front_paints_after_the_unit_behind_it()
    {
        var unit = new Tile(5, 5);
        var merged = BoardOrder.Merge([BoardOrder.Key(unit, BoardLayer.Top), BoardOrder.Key(new Tile(6, 5), BoardLayer.Side)],
            [BoardOrder.Key(unit, BoardLayer.Unit)]);

        Assert.Equal([0, ~0, 1], merged);
    }

    [Fact]
    public void Merging_keeps_both_lists_in_order_and_loses_nothing()
    {
        Assert.Equal([0, ~0, 1, ~1, ~2, 2], BoardOrder.Merge([1, 3, 9], [2, 5, 7]));
        Assert.Equal([0, 1], BoardOrder.Merge([1, 2], []));
        Assert.Equal([~0], BoardOrder.Merge([], [1]));
    }

    [Fact]
    public void A_place_between_tiles_projects_between_them_and_sorts_with_the_nearer_one()
    {
        var (from, to) = (new Tile(3, 4), new Tile(4, 4));
        var (a, b) = (UnitPlace.On(River, from), UnitPlace.On(River, to));
        var halfway = new UnitPlace((a.Map + b.Map) / 2, (a.Height + b.Height) / 2);

        var unit = One(null, Unit("a", "Party", from.X, from.Y));
        var moved = UnitPlan.Build(Battle(null, Unit("a", "Party", from.X, from.Y)), River, Art.Value, Fit,
            new Dictionary<UnitId, UnitPlace> { [new UnitId("a")] = halfway })[0];

        Assert.Equal((a.Feet + b.Feet) / 2, moved.Feet);
        Assert.Equal(BoardOrder.Key(to, BoardLayer.Unit), moved.Order);
        Assert.Equal(to, halfway.Over);
        Assert.Equal(from, a.Over);
        Assert.NotEqual(unit.Feet, moved.Feet);
    }

    [Theory]
    [InlineData(5, 5, 5, 7, 0, true)]    // +V: lower left on the screen
    [InlineData(5, 5, 7, 5, 0, false)]   // +U: lower right
    [InlineData(5, 5, 3, 5, 1, false)]   // -U: upper left
    [InlineData(5, 5, 5, 3, 1, true)]    // -V: upper right
    [InlineData(5, 5, 8, 8, 0, false)]   // straight down the screen: the art's own way
    public void A_unit_faces_its_foe_by_the_screen_direction(int x, int y, int foeX, int foeY, int row, bool mirror)
    {
        var unit = One(null, Unit("a", "Party", x, y), Unit("foe", "Enemy", foeX, foeY));

        Assert.Equal((row, mirror), (unit.Row, unit.Mirror));
    }

    [Fact]
    public void A_unit_faces_the_nearest_foe()
    {
        var unit = One(null, Unit("a", "Party", 5, 5), Unit("far", "Enemy", 12, 5), Unit("near", "Enemy", 5, 3));

        Assert.Equal((1, true), (unit.Row, unit.Mirror));
    }

    [Fact]
    public void Allies_are_not_foes_and_a_unit_with_no_foes_faces_the_viewer()
    {
        var unit = One(null, Unit("a", "Party", 5, 5), Unit("b", "Party", 3, 5));

        Assert.Equal((0, false), (unit.Row, unit.Mirror));
    }

    [Fact]
    public void Out_and_dead_units_are_not_drawn_nor_faced()
    {
        var units = Plan(null, Unit("a", "Party", 5, 5), Unit("gone", "Party", 6, 5, life: "Out"), Unit("dead", "Enemy", 3, 5, life: "Dead"));

        Assert.Equal(["a"], units.Select(u => u.Id.Value));
        Assert.Equal((0, false), (units[0].Row, units[0].Mirror));
    }

    [Fact]
    public void A_unit_off_the_map_is_left_out_rather_than_crashing()
        => Assert.Empty(Plan(null, Unit("a", "Party", 99, 99)));

    [Theory]
    [InlineData("Party", UnitTone.Party)]
    [InlineData("Enemy", UnitTone.Enemy)]
    [InlineData("Neutral", UnitTone.Neutral)]
    [InlineData("Something new", UnitTone.Neutral)]
    public void Sides_have_tones(string side, UnitTone tone)
        => Assert.Equal(tone, One(null, Unit("a", side, 5, 5)).Tone);

    [Theory]
    [InlineData(10, 10, 26)]
    [InlineData(5, 10, 13)]
    [InlineData(1, 100, 3)]    // anything alive shows at least the bar's end
    [InlineData(0, 10, 0)]
    [InlineData(40, 10, 26)]   // more than the maximum can't overflow
    [InlineData(5, 0, 0)]
    public void A_bars_fill_follows_the_manifests_rule(int hp, int max, int fill)
    {
        var unit = One(null, Unit("a", "Party", 5, 5, hp: hp, maxHp: max));

        Assert.Equal(UnitPlan.BarWidth, unit.Bar!.Width);
        Assert.Equal(fill, unit.Bar.Fill);
    }

    [Fact]
    public void The_bar_sits_over_the_head_centred_on_the_feet()
    {
        var unit = One(null, Unit("a", "Party", 5, 5));
        var head = unit.Feet.Y - unit.Sprite.AnchorY + unit.Sprite.HeadTop;

        Assert.Equal(unit.Feet.X - UnitPlan.BarWidth / 2f, unit.Bar!.TopLeft.X);
        Assert.True(unit.Bar.TopLeft.Y + UnitPlan.BarHeight <= head);
    }

    [Fact]
    public void On_a_raised_back_tile_the_marks_stay_under_the_top_strip()
    {
        var map = Pack.Map("river_crossing");
        var raised = new BoardTerrain(map, new BoardChanges([], [new HeightChange(new Tile(0, 0), 9)], []));
        var view = Battle("a", Unit("a", "Party", 0, 0, conditions: ["poisoned"]));

        var unit = UnitPlan.Build(view, raised, Art.Value, Fit)[0];

        var top = Fit.ToScreen(unit.ArrowAt!.Value with { Y = unit.ArrowAt.Value.Y - Art.Value.ArrowAnchorY });
        Assert.True(top.Y >= BoardFit.Area.Y, $"the arrow's top is at {top.Y}");
        Assert.True(unit.IconsAt.Y > unit.ArrowAt.Value.Y);
        Assert.True(unit.Bar!.TopLeft.Y > unit.IconsAt.Y);
    }

    [Fact]
    public void A_downed_unit_lies_down_with_its_countdown_and_no_bar()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, hp: 0, life: "Downed", countdown: 2));

        Assert.Equal(62, unit.Anim.Frames[0]);
        Assert.Null(unit.Bar);
        Assert.Equal("2", unit.Countdown!.Text);
        Assert.Equal(unit.Feet.X, unit.Countdown.Centre.X);
        Assert.True(unit.Countdown.Centre.Y < unit.Feet.Y);
    }

    [Fact]
    public void A_downed_unit_does_not_say_prone_since_it_is_already_lying_down()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, hp: 0, life: "Downed", countdown: 3, conditions: ["prone", "poisoned"]));

        Assert.Single(unit.Icons);
    }

    [Fact]
    public void A_downed_monster_holds_its_last_down_frame()
        => Assert.Equal(22, One(null, Unit("a", "Enemy", 5, 5, "bosses.troll.troll", life: "Downed", countdown: 1)).Anim.FrameAt(0));

    [Fact]
    public void A_standing_unit_idles_and_units_do_not_bob_in_step()
    {
        var units = Plan(null, Unit("a", "Party", 5, 5), Unit("b", "Enemy", 7, 7));

        Assert.Equal(units[0].Sprite.Idle, units[0].Anim);
        Assert.Null(units[0].Countdown);
        Assert.NotEqual(units[0].Phase, units[1].Phase);
    }

    [Fact]
    public void Only_the_active_unit_gets_the_ring_and_the_arrow()
    {
        var units = Plan("a", Unit("a", "Party", 5, 5), Unit("b", "Enemy", 7, 7));

        Assert.True(units.Single(u => u.Id.Value == "a").Active);
        Assert.NotNull(units.Single(u => u.Id.Value == "a").ArrowAt);
        Assert.False(units.Single(u => u.Id.Value == "b").Active);
        Assert.Null(units.Single(u => u.Id.Value == "b").ArrowAt);
    }

    [Fact]
    public void A_wide_sprite_has_a_wider_ring()
    {
        var goblin = One(null, Unit("a", "Enemy", 5, 5));
        var dragon = One(null, Unit("a", "Enemy", 5, 5, "bosses.dragon.red"));

        Assert.True(dragon.Ring > goblin.Ring);
        Assert.True(goblin.Ring < BoardProjection.HalfWidth);
    }

    [Fact]
    public void Conditions_show_their_art_and_those_without_any_show_nothing()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, conditions: ["poisoned", "hasted", "lit", "prone"]));

        Assert.Equal(3, unit.Icons.Length);                                  // poisoned, hasted, prone: lit has no art
        Assert.Equal("status/poisoned.png", Assert.Single(unit.Over).Path);
        Assert.Equal("status/hasted.png", Assert.Single(unit.Under).Path);   // prone has only an icon
    }

    [Fact]
    public void Two_conditions_sharing_art_show_it_once()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, conditions: ["paralysed", "incapacitated", "stunned"]));

        Assert.Single(unit.Icons);
        Assert.Single(unit.Over);
    }

    [Fact]
    public void The_icon_row_is_centred_and_capped()
    {
        ImmutableArray<string> many = ["poisoned", "burning", "bleeding", "slowed", "marked", "inspired", "charmed"];

        var unit = One(null, Unit("a", "Party", 5, 5, conditions: many));

        Assert.Equal(UnitPlan.MaxIcons, unit.Icons.Length);
        Assert.Equal(unit.Feet.X - unit.Icons.Length * UnitPlan.IconStep / 2f, unit.IconsAt.X);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("characters.nobody")]
    [InlineData("gibberish")]
    public void A_missing_sprite_falls_back_to_a_stand_in_for_its_side(string? key)
    {
        Assert.Equal(Art.Value.StandIn(UnitTone.Party), One(null, Unit("a", "Party", 5, 5, key)).Sprite);
        Assert.Equal(Art.Value.StandIn(UnitTone.Enemy), One(null, Unit("a", "Enemy", 5, 5, key)).Sprite);
    }
}
