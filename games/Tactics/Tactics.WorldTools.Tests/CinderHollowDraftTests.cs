using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class CinderHollowDraftTests
{
    [Fact]
    public void Each_chamber_is_forced_in_order_and_the_last_approach_has_a_safe_fire()
    {
        var rules = BaseContent.Rules;
        var region = rules.RegionFor("cinder_hollow")!;
        var walk = new RegionWalk(rules.GroundFor("cinder_hollow")!);
        var start = region.Def.Spawns.Start;
        var names = new[] { "hollow_throat", "hollow_diggings", "hollow_nest", "hollow_lair" };
        foreach (var name in names)
        {
            var stamp = region.Def.Stamps.Single(s => s.Map == name);
            var map = rules.MapFor(name)!;
            var blocked = (from y in Enumerable.Range(0, map.Height) from x in Enumerable.Range(0, map.Width)
                           select new Tile(stamp.At.X + x, stamp.At.Y + y)).ToHashSet();
            var reached = walk.From(start, blocked);
            Assert.DoesNotContain(new Tile(10, 32), reached);
            foreach (var later in region.Def.Beats.SkipWhile(b => b.Beat != name).Skip(1))
                Assert.DoesNotContain(later.At, reached);
        }
        var all = walk.From(start);
        Assert.Contains(new Tile(10, 32), all);
        Assert.Contains(new Tile(28, 32), all);
        Assert.Equal("entrance", region.Def.Spawns.Named!.Keys.Single());
        var fire = Assert.Single(rules.InteractablesIn("cinder_hollow"), t => t.Kind == "campfire");
        Assert.Equal(new Tile(28, 32), fire.At);
        // Two future arenas sit before the Nest. The fire is after it; no arena lies on the final approach.
        var draft = CinderHollowDraft.Build(rules);
        foreach (var (left, top, width, height) in new[] { (22, 6, 10, 8), (35, 17, 12, 8) })
            for (var y = top; y < top + height; y++)
                for (var x = left; x < left + width; x++)
                    Assert.Equal(TileFlags.None, draft.Get(new Tile(x, y))!.Flags);
    }
}
