using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Xunit;
using static Tactics.Presentation.Tests.World.StageScript;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="BattleIntro"/>: the walk-in, read from the first view of a fight in the world.</summary>
public sealed class BattleIntroTests
{
    private static Tile World(Tile local) => new(local.X + Origin.X, local.Y + Origin.Y);

    [Fact]
    public void Heroes_take_the_shortest_legal_path_to_their_slots()
    {
        var script = new StageScript();
        var intro = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.False(intro.Fades);
        var hero1 = Assert.Single(intro.Heroes, h => h.Unit.Value == "hero1");
        Assert.Equal("p1", hero1.Walker);
        Assert.Equal(new Tile(12, 12), hero1.Path[0]);
        Assert.Equal(World(Hero1), hero1.Path[^1]);
        Assert.Equal(5, hero1.Steps);
        Assert.All(hero1.Path.Zip(hero1.Path.Skip(1)), p => Assert.Equal(1, Math.Abs(p.First.X - p.Second.X) + Math.Abs(p.First.Y - p.Second.Y)));
        Assert.Equal(StageTuning.WalkInMost / 5, intro.Step, 9);
    }

    [Fact]
    public void A_path_goes_round_a_blocking_prop()
    {
        var crate = new PropPlacement("crate", new Tile(0, 2), true, Cover.ThreeQuarters);     // on the stamp's edge, in hero1's way
        var script = new StageScript(null, crate);
        var intro = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        var hero1 = Assert.Single(intro.Heroes, h => h.Unit.Value == "hero1");
        Assert.DoesNotContain(World(crate.At), hero1.Path);
        Assert.Equal(7, hero1.Steps);
        Assert.All(hero1.Path, t => Assert.True(script.Terrain.Rules.Walkable(t)));

        // And it's walked that way: at no moment is hero1 drawn over the crate's tile.
        for (var t = 0.0; t < intro.WalkEnd; t += 0.01)
            if (intro.At(t, script.Terrain).Places.TryGetValue(new UnitId("hero1"), out var place))
                Assert.NotEqual(crate.At, new Tile((int)MathF.Floor(place.Map.X), (int)MathF.Floor(place.Map.Y)));
    }

    [Fact]
    public void A_far_party_fades_in()
    {
        var script = new StageScript();
        var near = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(4, 13)));     // 13 steps
        var far = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(3, 13)));      // 14 steps
        var unseen = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, new Dictionary<string, UnitPlace>());

        Assert.False(near.Fades);
        Assert.True(far.Fades);
        Assert.True(unseen.Fades);
        Assert.Equal(StageTuning.FadeDown + StageTuning.FadeUp, far.WalkEnd, 9);
        Assert.Equal(0, far.At(0, script.Terrain).Fade);
        Assert.Equal(1, far.At(StageTuning.FadeDown, script.Terrain).Fade, 3);
        Assert.Equal(0, far.At(far.WalkEnd, script.Terrain).Fade, 3);
        // Before the dark they stand where they were; after it, on their slots.
        Assert.Equal(new Vector2(12.5f - Origin.X, 12.5f - Origin.Y), far.At(0.1, script.Terrain).Places[new UnitId("hero1")].Map);
        Assert.False(far.At(0.1, script.Terrain).Dark);
        var after = far.At(StageTuning.FadeDown + 0.01, script.Terrain);
        Assert.True(after.Dark);
        Assert.DoesNotContain(after.Places.Keys, k => k.Value.StartsWith("hero", StringComparison.Ordinal));
        Assert.Contains(new UnitId("hero1"), unseen.At(0.1, script.Terrain).Hidden);
    }

    // Ashen Ford's party_spawn is on the west bank: a party from the hills would run through the goblins (plan 5c, default 3).
    [Fact]
    public void A_path_past_an_enemy_fades()
    {
        var script = new StageScript();
        var units = Units().Select(u => u.Id.Value == "enemy1" ? u with { At = new Tile(-2, 0) } : u).ToImmutableArray();     // off the stamp, in the way
        var past = BattleIntro.Plan(Battle(units: units), script.Stage, script.Terrain, script.Walkers(new Tile(13, 10), new Tile(12, 13)));
        var clear = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(13, 10), new Tile(12, 13)));

        Assert.True(past.Fades);
        Assert.False(clear.Fades);
    }

    [Fact]
    public void Enemies_step_in_from_a_tile_away_from_the_party_one_after_another()
    {
        var script = new StageScript();
        var intro = BattleIntro.Plan(Battle(), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.Equal(["ally1", "enemy1", "enemy2"], intro.Others.Select(o => o.Unit.Value));
        Assert.All(intro.Others, o => Assert.Equal(1, Math.Abs(o.From.X - o.To.X) + Math.Abs(o.From.Y - o.To.Y)));
        Assert.Equal(World(Goblin1) with { X = World(Goblin1).X + 1 }, intro.Others[1].From);     // the party is to the west
        Assert.Equal(intro.WalkEnd, intro.Others[0].Start, 9);
        Assert.All(intro.Others.Zip(intro.Others.Skip(1)), p => Assert.Equal(StageTuning.StepInStagger, p.Second.Start - p.First.Start, 9));
    }

    [Fact]
    public void A_crowd_steps_in_closer_together()
    {
        var script = new StageScript();
        ImmutableArray<UnitView> crowd = [.. Units().Take(2), .. Enumerable.Range(0, 8).Select(i => Unit($"enemy{i + 1}", "Enemy", new Tile(5 + i % 3, i / 3 * 2)))];
        var intro = BattleIntro.Plan(Battle(units: crowd), script.Stage, script.Terrain, script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.Equal(8, intro.Others.Length);
        Assert.True(intro.Others[^1].End - intro.Others[0].Start <= StageTuning.StepInMost + 1e-9);
        Assert.True(intro.Others[1].Start - intro.Others[0].Start < StageTuning.StepInStagger);
    }

    // The property: whatever the ground, the party and the foes, the walk-in keeps to the plan's maxima and is over before the game's 3 s wait.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_walk_in_never_runs_over_its_maxima(int seed)
    {
        var random = new Random(seed);
        TileInfo Ground(int x, int y) => random.Next(9) == 0 ? StageWorld.Flat(0, StageWorld.Rock, TileFlags.Impassable) : StageWorld.Flat(random.Next(0, 3));
        var script = new StageScript(Ground);
        Tile Any() => new(random.Next(40), random.Next(30));
        var stamp = Enumerable.Range(0, 48).Select(i => new Tile(i % 8, i / 8)).OrderBy(_ => random.Next()).ToArray();
        var count = random.Next(0, 10);
        ImmutableArray<UnitView> units = [Unit("hero1", "Party", stamp[0]), Unit("hero2", "Party", stamp[1]),
            .. Enumerable.Range(0, count).Select(i => Unit($"enemy{i + 1}", random.Next(4) == 0 ? "Party" : "Enemy", stamp[2 + i]))];
        var intro = BattleIntro.Plan(Battle(units: units), script.Stage, script.Terrain, script.Walkers(Any(), Any()));

        Assert.True(intro.WalkEnd <= StageTuning.WalkInMost + 1e-9);
        Assert.True(intro.GridEnd <= intro.WalkEnd + StageTuning.GridIn + 1e-9);
        Assert.All(intro.Others, o => Assert.True(o.Start >= intro.WalkEnd - 1e-9 && o.End <= intro.WalkEnd + StageTuning.StepInMost + 1e-9));
        Assert.True(intro.End <= StageTuning.FormingMost + 1e-9);
        Assert.True(StageTuning.FormingMost < new TacticsTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero).FormUp.TotalSeconds);
        Assert.All(intro.Heroes, h => Assert.True(h.Steps < StageTuning.FarSteps));
        for (var t = 0.0; t <= intro.End + 0.05; t += 0.05)
        {
            var frame = intro.At(t, script.Terrain);
            Assert.InRange(frame.Fade, 0, 1);
            Assert.InRange(frame.GridAlpha, 0, 1);
        }
        var done = intro.At(intro.End, script.Terrain);
        Assert.Empty(done.Places);
        Assert.Empty(done.Hidden);
        Assert.Equal(1, done.GridAlpha);
        Assert.Equal(0, done.Fade, 3);
    }

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 60)];

    [Fact]
    public void Only_heroes_with_a_walker_walk()
    {
        Assert.Equal("p2", BattleIntro.WalkerOf(new UnitId("hero2"), ["p1", "p2"]));
        Assert.Null(BattleIntro.WalkerOf(new UnitId("hero3"), ["p1", "p2"]));
        Assert.Null(BattleIntro.WalkerOf(new UnitId("hero0"), ["p1", "p2"]));
        Assert.Null(BattleIntro.WalkerOf(new UnitId("ally1"), ["p1", "p2"]));
        Assert.Null(BattleIntro.WalkerOf(new UnitId("heroic"), ["p1", "p2"]));
    }
}
