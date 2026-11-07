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

    private static ImmutableArray<WalkerView> Walkers(params string[] ids)
        => [.. ids.Select((id, i) => new WalkerView(id, id, "characters.class_rogue", true, i == 0))];

    [Theory]
    [InlineData("bosses.air_elemental.gale", "bosses.special", .4)]
    [InlineData("bosses.air_elemental.gale", "bosses.cast", .4)]
    [InlineData("bosses.earth_elemental.stone", "bosses.special", .5)]
    [InlineData("bosses.earth_elemental.stone", "bosses.buff", .4)]
    [InlineData("bosses.fire_elemental.ember", "bosses.special", .4)]
    [InlineData("bosses.fire_elemental.ember", "bosses.cast", .4)]
    [InlineData("bosses.water_elemental.sea", "bosses.special", .4)]
    [InlineData("bosses.water_elemental.sea", "bosses.cast", .4)]
    [InlineData("bosses.dragon.red", "bosses.special", .5)]
    [InlineData("bosses.dragon.red", "bosses.cast", .5)]
    [InlineData("bosses.dragon.red", "bosses.buff", .7)]
    [InlineData("bosses.water_elemental.sea", "bosses.attack", .4)]
    [InlineData("bosses.dragon.red", "bosses.attack", .5)]
    [InlineData("bosses.air_elemental.storm", "bosses.special", .4)]
    [InlineData("bosses.air_elemental.storm", "bosses.cast", .4)]
    [InlineData("bosses.earth_elemental.magma", "bosses.special", .5)]
    [InlineData("bosses.earth_elemental.magma", "bosses.buff", .4)]
    [InlineData("bosses.water_elemental.swamp", "bosses.special", .4)]
    [InlineData("bosses.water_elemental.swamp", "bosses.cast", .4)]
    [InlineData("bosses.golem.basalt", "bosses.special", .5)]
    [InlineData("bosses.golem.basalt", "bosses.buff", .4)]
    [InlineData("bosses.golem.crystal", "bosses.special", .5)]
    [InlineData("bosses.golem.crystal", "bosses.buff", .4)]
    [InlineData("bosses.golem.temple", "bosses.special", .5)]
    [InlineData("bosses.golem.temple", "bosses.buff", .4)]
    [InlineData("bosses.fire_elemental.azure", "bosses.special", .4)]
    [InlineData("bosses.fire_elemental.azure", "bosses.cast", .4)]
    [InlineData("bosses.fire_elemental.radiant", "bosses.special", .4)]
    [InlineData("bosses.fire_elemental.radiant", "bosses.cast", .4)]
    [InlineData("bosses.fire_elemental.shadow", "bosses.special", .4)]
    [InlineData("bosses.fire_elemental.shadow", "bosses.cast", .4)]
    public void Elemental_health_bars_clear_their_active_wind_stone_and_flame_silhouettes(string key, string binding, double time)
    {
        var view = Unit("a", "Enemy", 11, 10, key);
        var posed = Assert.Single(UnitPlan.Build(Battle(null, view), River, Art.Value, Fit, poses:
            new Dictionary<UnitId, UnitPose> { [view.Id] = new(binding, time, new Vector2(0, 1)) }));
        var sprite = posed.Sprite;
        var source = sprite.Source(Assert.Single(posed.Anim.Frames), posed.Row);
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        var first = source.Height;
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (pixels[((source.Y + y) * width + source.X + x) * 4 + 3] >= 128) first = Math.Min(first, y);
        var highest = posed.Feet.Y + (first - sprite.AnchorY) * sprite.PixelScale;
        Assert.True(posed.Bar!.TopLeft.Y + UnitPlan.BarHeight < highest);
        Assert.Equal(One(null, view).Sprite.HeadLift, sprite.HeadLift);
    }

    [Theory]
    [InlineData("bosses.troll.troll", "bosses.special")]
    [InlineData("bosses.golem.granite", "bosses.special")]
    [InlineData("bosses.mimic.oak", "bosses.attack")]
    public void Raised_boulders_fists_and_jaws_stay_below_their_health_bar(string key, string binding)
    {
        var view = Unit("a", "Enemy", 11, 10, key);
        var idle = One(null, view);
        var posed = Assert.Single(UnitPlan.Build(Battle(null, view), River, Art.Value, Fit, poses:
            new Dictionary<UnitId, UnitPose> { [view.Id] = new(binding, .3, new Vector2(0, 1)) }));
        var sprite = posed.Sprite;
        var source = sprite.Source(Assert.Single(posed.Anim.Frames), posed.Row);
        var (pixels, width, _) = PngPixels.Read(Path.Combine(Pack.Root, sprite.Path));
        var first = source.Height;
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (pixels[((source.Y + y) * width + source.X + x) * 4 + 3] >= 128) first = Math.Min(first, y);
        var highest = posed.Feet.Y + (first - sprite.AnchorY) * sprite.PixelScale;
        Assert.True(posed.Bar!.TopLeft.Y + UnitPlan.BarHeight < highest);
        Assert.True(posed.Bar.TopLeft.Y < idle.Bar!.TopLeft.Y);
        Assert.Equal(idle.Sprite.HeadLift, posed.Sprite.HeadLift); // headroom does not change body size
    }

    [Fact]
    public void Walkers_have_no_bars_and_only_the_leader_a_crown()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 1), ["b"] = new(new Vector2(2.5f, 3.5f), 0) };
        var poses = new Dictionary<string, UnitPose> { ["a"] = new("idle", 3, new Vector2(1, 0)), ["b"] = new("idle", 3, new Vector2(1, 0)) };

        var units = UnitPlan.Walkers(Walkers("a", "b"), places, poses, Art.Value);

        Assert.All(units, u => Assert.Null(u.Bar));
        Assert.All(units, u => Assert.False(u.Active));
        var crowned = Assert.Single(units, u => u.CrownAt is not null);
        Assert.Equal("a", crowned.Id.Value);
        Assert.True(crowned.CrownAt!.Value.Y < crowned.Feet.Y - 20);
        Assert.Equal(places["a"].Feet, crowned.Feet);
    }

    // Interaction review 5b, item 11: the party's green ring faded into grass. A walker's ring now goes over a dark one; in a fight only a
    // hero with a player's colour has one, so a hero's ring is the same in the world and in a fight.
    [Fact]
    public void A_walkers_ring_is_outlined_and_a_fights_only_for_a_coloured_hero()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 0), ["b"] = new(new Vector2(2.5f, 3.5f), 0) };

        Assert.All(UnitPlan.Walkers(Walkers("a", "b"), places, new Dictionary<string, UnitPose>(), Art.Value), u => Assert.True(u.Outlined));
        Assert.False(One(null, Unit("ally1", "Party", 3, 3)).Outlined);
        Assert.False(One(null, Unit("goblin", "Enemy", 3, 3)).Outlined);
        Assert.True(One(null, Unit("hero1", "Party", 3, 3) with { Color = "#7ccbff" }).Outlined);
    }

    // The user's answer 14: a walker's ring is their player's colour; with none, or one that isn't a colour, it's the party's green.
    [Fact]
    public void A_walkers_ring_is_their_players_colour_else_the_partys_green()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 0), ["b"] = new(new Vector2(2.5f, 3.5f), 0),
            ["c"] = new(new Vector2(1.5f, 3.5f), 0), ["d"] = new(new Vector2(1.5f, 2.5f), 0) };
        WalkerView[] party = [new("a", "a", "characters.class_rogue", true, true, "#7ccbff"), new("b", "b", "characters.class_rogue", false, false, "#FFD23F"),
            new("c", "c", "characters.class_rogue", true, false), new("d", "d", "characters.class_rogue", true, false, "teal")];

        var rings = UnitPlan.Walkers(party, places, new Dictionary<string, UnitPose>(), Art.Value).ToDictionary(u => u.Id.Value, u => u.RingColour);

        Assert.Equal(Rgb.Hex("#7ccbff"), rings["a"]);
        Assert.Equal(Rgb.Hex("#ffd23f"), rings["b"]);       // away, still theirs: the canvas dims it as it dims them
        Assert.Equal(UnitPlan.PartyRing, rings["c"]);
        Assert.Equal(UnitPlan.PartyRing, rings["d"]);
    }

    // In a fight a hero wears their colour, a companion the party's green, a goblin its red. A hero keeps their colour on their own turn
    // (the turn's gold hid whose it was, and matched the palette's Gold); anyone else has the turn's gold on theirs.
    [Theory]
    [InlineData("hero2")]
    [InlineData("ally1")]
    [InlineData("goblin")]
    public void A_fights_rings_are_the_heros_colour_the_sides_and_the_turns_gold(string active)
    {
        UnitView[] units = [Unit("hero1", "Party", 3, 3) with { Color = "#ffab94" }, Unit("hero2", "Party", 4, 3) with { Color = "#8ff0c4" },
            Unit("ally1", "Party", 5, 3), Unit("goblin", "Enemy", 9, 5) with { Color = "#ffab94" }];

        var rings = Plan(active, units).ToDictionary(u => u.Id.Value);

        Assert.Equal(Rgb.Hex("#ffab94"), rings["hero1"].RingColour);
        Assert.Equal(Rgb.Hex("#8ff0c4"), rings["hero2"].RingColour);
        Assert.Equal(active == "ally1" ? UnitPlan.ActiveRing : UnitPlan.PartyRing, rings["ally1"].RingColour);
        Assert.Equal(active == "goblin" ? UnitPlan.ActiveRing : UnitPlan.EnemyRing, rings["goblin"].RingColour);   // a colour on a foe is never drawn
        Assert.Null(rings["goblin"].Colour);
        Assert.True(rings[active].Active);
    }

    [Fact]
    public void A_walker_whose_phone_is_gone_is_away()
    {
        WalkerView[] party = [new("a", "a", "characters.class_rogue", true, true), new("b", "b", "characters.class_rogue", false, false)];
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 0), ["b"] = new(new Vector2(2.5f, 3.5f), 0) };

        var units = UnitPlan.Walkers(party, places, new Dictionary<string, UnitPose>(), Art.Value);

        Assert.Equal(["b"], units.Where(u => u.Away).Select(u => u.Id.Value));
    }

    [Fact]
    public void A_walker_and_its_crown_go_on_the_pixels_they_are_given()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 1) };
        var poses = new Dictionary<string, UnitPose> { ["a"] = new("walk", 0.25, new Vector2(1, 0)) };

        var plain = Assert.Single(UnitPlan.Walkers(Walkers("a"), places, poses, Art.Value));
        var moved = Assert.Single(UnitPlan.Walkers(Walkers("a"), places, poses, Art.Value, (art, standing) => standing ? art : art + new Vector2(5, 7)));

        Assert.Equal(plain.Feet + new Vector2(5, 7), moved.Feet);
        Assert.Equal(plain.CrownAt + new Vector2(5, 7), moved.CrownAt);
        Assert.Equal(plain.Order, moved.Order);
    }

    [Fact]
    public void A_standing_walker_is_said_to_be_standing()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 1) };
        var poses = new Dictionary<string, UnitPose> { ["a"] = new("idle", 0.25, new Vector2(1, 0)) };

        var moved = Assert.Single(UnitPlan.Walkers(Walkers("a"), places, poses, Art.Value, (art, standing) => standing ? art + new Vector2(1, 1) : art));

        Assert.Equal(places["a"].Feet + new Vector2(1, 1), moved.Feet);
    }

    [Fact]
    public void The_crown_is_the_one_the_emote_manifest_says()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack.Root, "emotes", "manifest.json")));
        var root = manifest.RootElement;
        var crown = Art.Value.Crown!;

        Assert.Equal(root.GetProperty("emotes").GetProperty("crown").GetProperty("row").GetInt32(), crown.Row);
        Assert.Equal(root.GetProperty("frame")[0].GetInt32(), crown.Width);
        Assert.Equal(root.GetProperty("anchor")[1].GetInt32(), crown.AnchorY);
        Assert.True(File.Exists(Path.Combine(Pack.Root, crown.Path)));
    }

    [Fact]
    public void The_crown_plays_through_once_then_loops_its_tail()
    {
        var crown = new EmoteStrip("e.png", 20, 20, 8, 19, 11, Frames: 4, Fps: 8, LoopStart: 2);

        Assert.Equal([0, 1, 2, 3, 2, 3, 2], Enumerable.Range(0, 7).Select(i => crown.FrameAt(i / 8.0 + 0.01)));
        Assert.Equal(new ArtRect(60, 220, 20, 20), crown.Source(3));
    }

    [Fact]
    public void Walkers_are_in_the_painters_order()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(5.5f, 5.5f), 0), ["b"] = new(new Vector2(2.5f, 2.5f), 0) };

        var units = UnitPlan.Walkers(Walkers("a", "b"), places, new Dictionary<string, UnitPose>(), Art.Value);

        Assert.Equal(["b", "a"], units.Select(u => u.Id.Value));
    }

    [Fact]
    public void A_walker_with_no_place_isnt_drawn()
        => Assert.Empty(UnitPlan.Walkers(Walkers("a"), new Dictionary<string, UnitPlace>(), new Dictionary<string, UnitPose>(), Art.Value));

    [Theory]
    [InlineData(1, 0, 0, false)]
    [InlineData(0, 1, 0, true)]
    [InlineData(0, -1, 1, true)]
    [InlineData(-1, 0, 1, false)]
    public void A_walker_shows_the_facing_its_pose_says(float x, float y, int row, bool mirror)
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 0) };
        var poses = new Dictionary<string, UnitPose> { ["a"] = new("walk", 0.25, new Vector2(x, y)) };

        var unit = Assert.Single(UnitPlan.Walkers(Walkers("a"), places, poses, Art.Value));

        Assert.Equal((row, mirror), (unit.Row, unit.Mirror));
        Assert.Equal(Pack.Frames("characters", "walk")[3], unit.Anim.FrameAt(0));
    }

    [Fact]
    public void An_idle_walker_keeps_breathing_on_the_canvass_clock()
    {
        var places = new Dictionary<string, UnitPlace> { ["a"] = new(new Vector2(3.5f, 3.5f), 0) };
        var poses = new Dictionary<string, UnitPose> { ["a"] = new("idle", 2, new Vector2(1, 0)) };

        var unit = Assert.Single(UnitPlan.Walkers(Walkers("a"), places, poses, Art.Value));

        Assert.True(unit.Anim.Loop);
        Assert.True(unit.Anim.Frames.Length > 1);
    }

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
    [InlineData(5, 5, 5, 7, 1, false)]   // +V: south-west, authored view
    [InlineData(5, 5, 7, 5, 7, false)]   // +U: south-east
    [InlineData(5, 5, 3, 5, 3, false)]   // -U: north-west
    [InlineData(5, 5, 5, 3, 5, false)]   // -V: north-east
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

        Assert.Equal((5, false), (unit.Row, unit.Mirror));
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
        var head = unit.Feet.Y - unit.Sprite.HeadLift;

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

        Assert.Equal(13, unit.Anim.Frames[0]);
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
        => Assert.Equal(Pack.Frames("bosses", "down")[^1], One(null, Unit("a", "Enemy", 5, 5, "bosses.troll.frost", life: "Downed", countdown: 1)).Anim.FrameAt(0));

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
    public void A_wide_sprite_has_a_wider_ring_but_it_stays_inside_the_tile()
    {
        var goblin = One(null, Unit("a", "Enemy", 5, 5));
        var dragon = One(null, Unit("a", "Enemy", 5, 5, "bosses.dragon.red"));

        Assert.True(dragon.Ring > goblin.Ring);
        // An ellipse of radius r by r/2 fits in the diamond while r * sqrt(2) reaches no further than the corner: past that, front tiles cut it.
        Assert.True(dragon.Ring * MathF.Sqrt(2) <= BoardProjection.HalfWidth + 0.01f);
    }

    [Fact]
    public void A_units_bob_comes_from_its_id_so_a_unit_leaving_does_not_jump_the_others()
    {
        var (a, b) = (Unit("a", "Party", 5, 5), Unit("b", "Enemy", 7, 7));

        var both = Plan(null, a, b).Single(u => u.Id.Value == "b");
        var alone = Plan(null, b).Single();

        Assert.Equal(both.Phase, alone.Phase);
    }

    [Theory]
    [InlineData(4.9f, 5.5f, 5, 5)]    // -X: not yet past the middle of the way from (4, 5) to (5, 5)... nearer (5, 5)
    [InlineData(4.1f, 5.5f, 4, 5)]    // -X: nearer (4, 5)
    [InlineData(5.5f, 4.9f, 5, 5)]    // +Y or -Y: nearer (5, 5)
    [InlineData(5.5f, 4.1f, 5, 4)]    // nearer (5, 4)
    [InlineData(5.5f, 5.5f, 5, 5)]    // on the tile
    [InlineData(5.9f, 5.5f, 6, 5)]    // +X past the middle: the front tile
    [InlineData(5.5f, 5.9f, 5, 6)]    // +Y past the middle: the front tile
    public void Between_tiles_a_unit_sorts_with_the_nearer_one_in_every_direction(float x, float y, int tileX, int tileY)
        => Assert.Equal(new Tile(tileX, tileY), new UnitPlace(new Vector2(x, y), 0).Over);

    [Fact]
    public void A_pose_plays_a_frame_of_its_animation_and_faces_its_way()
    {
        var unit = Unit("a", "Party", 5, 5);
        var sprite = Art.Value.Sprite(Goblin)!;

        var walking = UnitPlan.Build(Battle(null, unit), River, Art.Value, Fit, null,
            new Dictionary<UnitId, UnitPose> { [new UnitId("a")] = new UnitPose("walk", 0.3, new Vector2(1, 0)) })[0];

        Assert.Equal(sprite.Anim("walk").FrameAt(0.3), Assert.Single(walking.Anim.Frames));
        Assert.Equal(7, walking.Row);       // +X: authored south-east
        Assert.False(walking.Mirror);
    }

    [Theory]
    [InlineData(1, 0, 7, false)]     // +X, south-east
    [InlineData(0, 1, 1, false)]     // +Y, south-west
    [InlineData(-1, 0, 3, false)]    // -X, north-west
    [InlineData(0, -1, 5, false)]    // -Y, north-east
    public void A_poses_direction_picks_the_sheets_row_and_mirror(float x, float y, int row, bool mirror)
    {
        var posed = UnitPlan.Build(Battle(null, Unit("a", "Party", 5, 5)), River, Art.Value, Fit, null,
            new Dictionary<UnitId, UnitPose> { [new UnitId("a")] = new UnitPose("walk", 0, new Vector2(x, y)) })[0];

        Assert.Equal((row, mirror), (posed.Row, posed.Mirror));
    }

    [Fact]
    public void A_pose_with_no_direction_keeps_the_units_usual_facing()
    {
        var units = new[] { Unit("a", "Party", 5, 5), Unit("b", "Enemy", 7, 5) };
        var usual = Plan(null, units).Single(u => u.Id.Value == "a");

        var posed = UnitPlan.Build(Battle(null, units), River, Art.Value, Fit, null,
            new Dictionary<UnitId, UnitPose> { [new UnitId("a")] = new UnitPose("hurt", 0, null) }).Single(u => u.Id.Value == "a");

        Assert.Equal((usual.Row, usual.Mirror), (posed.Row, posed.Mirror));
    }

    [Fact]
    public void Conditions_show_their_art_and_those_without_any_show_nothing()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, conditions: ["poisoned", "hasted", "disengaged", "prone"]));

        Assert.Equal(3, unit.Icons.Length);                                  // poisoned, hasted, prone: disengaged has no art
        Assert.Equal("status/poisoned.png", Assert.Single(unit.Over).Path);
        Assert.Equal("status/hasted.png", Assert.Single(unit.Under).Path);   // prone has only an icon
    }

    [Fact]
    public void Two_conditions_sharing_art_show_it_once()
    {
        var unit = One(null, Unit("a", "Party", 5, 5, conditions: ["incapacitated", "stunned"]));

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
