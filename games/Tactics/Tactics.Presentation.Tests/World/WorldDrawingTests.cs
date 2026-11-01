using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary>What the world's canvas decides: what a frame may record, what goes over a unit, which halos it draws and which images it loads.</summary>
public sealed class WorldDrawingTests
{
    private static readonly Lazy<WorldGround> Ground = new(() => Pack.Ground("ashen_ford"));
    private static readonly Lazy<WorldPlan> Ford = new(() => WorldPlan.Build(new BoardTerrain(Ground.Value.Map, null), Pack.Art));

    private static double OneMsEach(BlockPlan block) => 1;

    [Fact]
    public void A_frame_takes_blocks_nearest_first_up_to_its_budget()
    {
        var queue = new BlockQueue(Ford.Value.Blocks);
        var aim = new Vector2(40, 40);
        var nearest = Ford.Value.Blocks.OrderBy(b => Vector2.DistanceSquared(b.Middle, aim)).Take(3).Select(b => b.Index).ToArray();

        var taken = queue.TakeWithin(aim, OneMsEach, 3);

        Assert.Equal(nearest.Order(), taken.Select(b => b.Index).Order());
        Assert.Equal(Ford.Value.Blocks.Length - 3, queue.Count);
    }

    [Fact]
    public void A_frame_always_takes_one_block_or_a_block_dearer_than_the_budget_would_never_be_drawn()
    {
        var queue = new BlockQueue(Ford.Value.Blocks);

        Assert.Single(queue.TakeWithin(Vector2.Zero, _ => 50, WorldTuning.BlockBudgetMs));
    }

    [Fact]
    public void An_empty_queue_takes_nothing() => Assert.Empty(new BlockQueue([]).TakeWithin(Vector2.Zero, OneMsEach, 4));

    [Fact]
    public void The_blocks_in_a_view_are_those_whose_art_crosses_it()
    {
        var plan = Ford.Value;
        var middle = BoardProjection.Corner(80, 60, 0);
        var view = new ArtArea(middle - new Vector2(480, 270), middle + new Vector2(480, 270));

        var seen = plan.BlocksIn(view);

        Assert.NotEmpty(seen);
        Assert.Equal(plan.Blocks.Where(b => !b.Plan.Items.IsEmpty && new ArtArea(b.Plan.Min, b.Plan.Max).Overlaps(view)).Select(b => b.Index), seen.Select(b => b.Index));
        Assert.True(seen.Length < plan.Blocks.Length);
    }

    [Fact]
    public void Over_points_at_the_same_items_as_items_over()
    {
        var plan = Ford.Value;
        var tile = new Tile(30, 65);
        var feet = plan.Terrain.Centre(tile);
        var area = new ArtArea(feet - new Vector2(16, 48), feet + new Vector2(16, 4));
        var after = BoardOrder.Key(new Tile(tile.X - 3, tile.Y - 3), BoardLayer.Unit);

        var found = plan.Over(area, after);

        Assert.NotEmpty(found);
        Assert.Equal(plan.ItemsOver(area, after), found.Select(at => plan.Blocks[at.Block].Lit.Items[at.Item]));
    }

    /// <summary>Round a block corner, what's repainted comes from several blocks, and a later block isn't a later item: only the sort puts them right.</summary>
    [Fact]
    public void What_goes_over_a_unit_at_a_block_corner_is_in_painters_order_not_block_order()
    {
        var plan = Ford.Value;
        var corner = new Tile(80, 64);
        var feet = plan.Terrain.Centre(corner);
        var area = new ArtArea(feet - new Vector2(48, 96), feet + new Vector2(48, 12));

        var found = plan.Over(area, 0);

        Assert.True(found.Select(at => at.Block).Distinct().Count() > 1);
        Assert.NotEqual(found.OrderBy(at => at.Block).ThenBy(at => at.Item), found);
        var orders = found.Select(plan.OrderOf).ToArray();
        Assert.Equal(orders.Order(), orders);
    }

    [Fact]
    public void Items_of_one_order_keep_theirs_with_several_units_round_them()
    {
        var plan = Ford.Value;
        var art = UnitArt.Load(Pack.Root);
        var party = new[] { new WalkerView("a", "a", "characters.class_rogue", true, true), new WalkerView("b", "b", "characters.class_rogue", true, false) };
        var places = new Dictionary<string, UnitPlace> { ["a"] = UnitPlace.On(plan.Terrain, new Tile(80, 64)), ["b"] = UnitPlace.On(plan.Terrain, new Tile(79, 63)) };
        var units = UnitPlan.Walkers(party, places, new Dictionary<string, UnitPose>(), art);

        var found = plan.Overlay(units);

        Assert.Equal(found.OrderBy(plan.OrderOf).ThenBy(at => at.Block).ThenBy(at => at.Item), found);
        Assert.Equal(found.Distinct(), found);
    }

    [Fact]
    public void A_view_is_worked_out_for_a_window_of_any_shape()
    {
        var fit = WorldView.Centred(new Vector2(100.25f, 50.25f));
        Assert.Equal(new Vector2(760, 440), fit.Origin);
        Assert.Equal(fit.Origin, WorldView.OriginIn(fit, new Vector2(1920, 1080)));
        Assert.Equal(fit.Origin + new Vector2(320, 0), WorldView.OriginIn(fit, new Vector2(2560, 1080)));

        var seen = WorldView.Seen(fit.Origin, new Vector2(1920, 1080));

        Assert.Equal(new Vector2(-380, -220), seen.Min);
        Assert.Equal(new Vector2(580, 320), seen.Max);
    }

    [Fact]
    public void Dropping_the_probes_leaves_what_goes_over_a_unit()
    {
        var plan = WorldPlan.Build(new BoardTerrain(Ground.Value.Map, null), Pack.Art);
        var feet = plan.Terrain.Centre(new Tile(30, 65));
        var area = new ArtArea(feet - new Vector2(16, 48), feet + new Vector2(16, 4));
        var before = plan.Over(area, 0);
        Assert.Contains(plan.Blocks.SelectMany(b => b.Lit.Items), i => i is LitFace { Probes.IsEmpty: false });

        plan.DropProbes();

        Assert.DoesNotContain(plan.Blocks.SelectMany(b => b.Lit.Items), i => i is LitFace { Probes.IsEmpty: false } or LitSprite { Probe: not null });
        Assert.Equal(before, plan.Over(area, 0));
    }

    [Fact]
    public void A_units_area_holds_its_sprite_and_its_ring_either_way_it_faces()
    {
        var art = UnitArt.Load(Pack.Root);
        var sprite = art.Sprite("characters.class_fighter")!;
        foreach (var mirror in new[] { false, true })
        {
            var feet = new Vector2(100, 200);
            var unit = new BoardUnit(0, new UnitId("u"), UnitTone.Party, feet, sprite, sprite.Idle, 0, 0, mirror, 40, false, [], [], null, null, [], Vector2.Zero, null);

            var area = WorldPlan.AreaOf(unit);

            var left = feet.X - (mirror ? sprite.Width - sprite.AnchorX : sprite.AnchorX);
            Assert.True(area.Min.X <= left && area.Max.X >= left + sprite.Width);
            Assert.True(area.Min.Y <= feet.Y - sprite.AnchorY && area.Max.Y >= feet.Y - sprite.AnchorY + sprite.Height);
            Assert.True(area.Min.X <= feet.X - 40 && area.Max.X >= feet.X + 40);
            Assert.True(area.Max.Y >= feet.Y + 20);
        }
    }

    [Fact]
    public void The_plan_lists_each_image_once_and_says_which_lie_flat()
    {
        var plan = Ford.Value;

        Assert.Equal(plan.Images.Length, plan.Images.Select(i => (i.Image.Key, i.Flat)).Distinct().Count());
        Assert.Contains(plan.Images, i => i.Flat && i.Image.Path.EndsWith("ash_grey.png", StringComparison.Ordinal));
        Assert.Contains(plan.Images, i => !i.Flat && i.Image.Frames == 1);
        Assert.Contains(plan.Images, i => i.Image.Frames > 1);
        Assert.Equal(plan.Paths, plan.Images.Select(i => i.Image.Path).Concat(plan.Halos).Distinct().Order(StringComparer.Ordinal));
    }

    private static BattleMap Torches()
    {
        var bytes = new byte[32 * 32 * 3];
        return BattleMap.FromBytes("test", 32, 32, bytes, ["grass/lush"],
            [new PropPlacement("torch", new Tile(2, 2), true, Cover.None), new PropPlacement("torch", new Tile(28, 28), true, Cover.None)],
            ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
    }

    [Fact]
    public void A_views_halos_are_those_it_can_see()
    {
        var plan = BoardPlan.Build(new BoardTerrain(Torches(), null), Pack.Art);
        var lighting = BoardLighting.Of(plan, "test", Pack.Art.Lighting);
        var all = lighting.Halos(1);
        Assert.Equal(2, all.Length);
        var near = all.MinBy(h => h.TopLeft.Y)!;

        var seen = lighting.Halos(1, near.TopLeft - new Vector2(10), near.TopLeft + new Vector2(10));

        Assert.Equal(near, Assert.Single(seen));
    }

    [Fact]
    public void A_halo_only_just_in_a_view_still_counts()
    {
        var plan = BoardPlan.Build(new BoardTerrain(Torches(), null), Pack.Art);
        var lighting = BoardLighting.Of(plan, "test", Pack.Art.Lighting);
        var halo = lighting.Halos(1).MinBy(h => h.TopLeft.Y)!;
        var edge = halo.TopLeft + halo.Size;

        Assert.Single(lighting.Halos(1, edge - new Vector2(1), edge + new Vector2(10)));
        Assert.Empty(lighting.Halos(1, edge + new Vector2(1), edge + new Vector2(10)));
    }

    [Fact]
    public void A_route_is_legal_steps_from_its_start_to_as_far_as_the_ground_goes()
    {
        var ground = Ground.Value;
        var start = new Tile(30, 65);

        var route = WorldRoute.Across(ground.Terrain, start, ClassDef.DefaultJump);

        Assert.Equal(start, route[0]);
        Assert.True(route.Length > 80, $"{route.Length} tiles");
        foreach (var (from, to) in route.Zip(route.Skip(1)))
            Assert.Contains(Enum.GetValues<WalkDir>().Select(d => WorldWalk.Next(ground.Terrain, from, d, ClassDef.DefaultJump, null)?.To), t => t == to);
    }

    [Fact]
    public void A_walker_on_a_route_is_between_two_tiles_and_stops_at_the_end()
    {
        var terrain = new BoardTerrain(Ground.Value.Map, null);
        ImmutableArray<Tile> route = [new Tile(30, 65), new Tile(31, 65), new Tile(32, 65)];

        var halfway = WorldRoute.At(terrain, route, 0.5f);
        var past = WorldRoute.At(terrain, route, 50);

        Assert.Equal(new Vector2(31, 65.5f), halfway.Map);
        Assert.Equal((terrain.Top(route[0]) + terrain.Top(route[1])) / 2, halfway.Height);
        Assert.Equal(UnitPlace.On(terrain, route[2]), past);
    }

    [Fact]
    public void A_stand_in_shows_on_the_tile_it_is_given()
    {
        var terrain = new BoardTerrain(Ground.Value.Map, null);
        var art = UnitArt.Load(Pack.Root);
        var place = UnitPlace.On(terrain, new Tile(30, 65));

        var units = DemoRegion.StandIn(terrain, art, place, new Vector2(1, 0), 0.25);

        Assert.Equal(place.Feet, Assert.Single(units).Feet);
    }
}
