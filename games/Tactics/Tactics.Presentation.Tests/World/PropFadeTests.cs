using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

public sealed class PropFadeTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones =
        ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    private static BattleMap Map(int width, int height, Func<int, int, int> heightAt, params PropPlacement[] props)
    {
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bytes[(y * width + x) * 3] = (byte)heightAt(x, y);
        return BattleMap.FromBytes("test", width, height, bytes, ["grass/lush"], [.. props], NoZones);
    }

    private static BoardUnit MakeWalker(long order, Vector2 feet)
    {
        var art = UnitArt.Load(Pack.Root);
        var sprite = art.Sprite("characters.class_fighter")!;
        return new BoardUnit(order, new UnitId("walker"), UnitTone.Party, feet, sprite, sprite.Idle, 0, 0, false, 20, false, [], [], null, null, [], Vector2.Zero, null);
    }

    [Fact]
    public void A_tree_in_front_of_a_walker_fades()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        // A walker behind the tree: lower order, standing at the tile behind or near the tree's feet so their areas overlap
        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);

        Assert.Equal(WorldTuning.PropFadeAlpha, fade.AlphaOf(0));
    }

    [Fact]
    public void A_tree_behind_a_walker_doesnt()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        // A walker in front of the tree: higher order
        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 6), BoardLayer.Unit), feet);

        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);

        Assert.Equal(1f, fade.AlphaOf(0));
    }

    [Fact]
    public void Only_trees_that_cover_a_walker_fade()
    {
        var tree1 = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var tree2 = new PropPlacement("tree", new Tile(12, 12), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree1, tree2), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);

        Assert.Equal(WorldTuning.PropFadeAlpha, fade.AlphaOf(0));
        Assert.Equal(1f, fade.AlphaOf(1));
    }

    [Fact]
    public void It_comes_back_when_the_party_has_gone()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);
        Assert.Equal(WorldTuning.PropFadeAlpha, fade.AlphaOf(0));

        fade.Update([], dt: WorldTuning.PropFadeSeconds);
        Assert.Equal(1f, fade.AlphaOf(0));
    }

    [Fact]
    public void A_party_split_across_a_tree_fades_it_once()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walkerBehind = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);
        var walkerInFront = MakeWalker(BoardOrder.Key(new Tile(5, 6), BoardLayer.Unit), feet);

        fade.Update([walkerBehind, walkerInFront], dt: WorldTuning.PropFadeSeconds);

        Assert.Equal(WorldTuning.PropFadeAlpha, fade.AlphaOf(0));
    }

    [Fact]
    public void The_fade_eases()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        // Halfway through PropFadeSeconds (0.1s out of 0.2s)
        fade.Update([walker], dt: WorldTuning.PropFadeSeconds / 2);
        var halfAlpha = fade.AlphaOf(0);

        Assert.True(halfAlpha < 1.0f && halfAlpha > WorldTuning.PropFadeAlpha, $"Expected easing between 0.35 and 1.0, got {halfAlpha}");
        Assert.InRange(halfAlpha, 0.65f, 0.70f);
    }

    [Fact]
    public void A_snap_fades_at_once()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        fade.Update([walker], dt: 0);
        fade.Snap();

        Assert.Equal(WorldTuning.PropFadeAlpha, fade.AlphaOf(0));
    }

    [Fact]
    public void Nothing_fades_in_a_fight()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan) { InFight = true };

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);

        Assert.Equal(1f, fade.AlphaOf(0));
    }

    [Fact]
    public void A_faded_tree_isnt_repainted_solid_over_a_walker()
    {
        var tree = new PropPlacement("tree", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, tree), null), Pack.Art);
        var fade = new PropFade(plan);

        var feet = BoardProjection.Corner(5, 5, 0) + new Vector2(16, 8);
        var walker = MakeWalker(BoardOrder.Key(new Tile(5, 4), BoardLayer.Unit), feet);

        // Find the tree's ItemAt
        var treeItem = default(ItemAt);
        var foundTree = false;
        foreach (var block in plan.Blocks)
        {
            for (var i = 0; i < block.Lit.Items.Length; i++)
            {
                if (block.Lit.Items[i] is LitSprite { IsTallProp: true })
                {
                    treeItem = new ItemAt(block.Index, i);
                    foundTree = true;
                    break;
                }
            }
        }
        Assert.True(foundTree);

        // Without fade, Overlay includes the tree because it occludes the walker
        var solidOverlay = plan.Overlay([walker]);
        Assert.Contains(treeItem, solidOverlay);

        // With fade, once the tree fades, Overlay skips it so it isn't repainted solid
        fade.Update([walker], dt: WorldTuning.PropFadeSeconds);
        var fadedOverlay = plan.Overlay([walker], fade);
        Assert.DoesNotContain(treeItem, fadedOverlay);
    }
}
