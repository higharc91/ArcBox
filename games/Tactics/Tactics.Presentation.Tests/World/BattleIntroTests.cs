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

    /// <summary>A party of four, close together west of the stamp, in a world with rocks about: what the hardware check's seed does.</summary>
    private static (StageScript Script, BattleIntro Intro, Dictionary<string, UnitPlace> Walkers, Tile[] Slots) Bunched(int seed)
    {
        var random = new Random(seed);
        TileInfo Ground(int x, int y) => x < Origin.X && random.Next(7) == 0 ? StageWorld.Flat(0, StageWorld.Rock, TileFlags.Impassable) : StageWorld.Flat(0);
        var script = new StageScript(Ground);
        var slots = Enumerable.Range(0, 24).Select(i => new Tile(i % 4, i / 4)).OrderBy(_ => random.Next()).Take(4).ToArray();
        ImmutableArray<UnitView> units = [.. slots.Select((t, i) => Unit($"hero{i + 1}", "Party", t)), Unit("enemy1", "Enemy", new Tile(7, 0)), Unit("enemy2", "Enemy", new Tile(7, 5))];
        var free = Enumerable.Range(0, 30).Select(i => new Tile(Origin.X - 1 - i % 5, Origin.Y + i / 5)).Where(t => script.Terrain.Rules.Walkable(t)).OrderBy(_ => random.Next()).ToArray();
        var stood = new Tile[4];
        for (var i = 0; i < 4; i++)
            stood[i] = i > 0 && random.Next(5) == 0 ? stood[random.Next(i)] : free[i];      // now and then two walkers stand together
        var walkers = stood.Select((t, i) => (Key: $"p{i + 1}", Place: new UnitPlace(new Vector2(t.X + 0.5f, t.Y + 0.5f), 0))).ToDictionary(w => w.Key, w => w.Place);
        var battle = Battle(units: units) with { Stage = new StageView(Region, Origin, 0, ["p1", "p2", "p3", "p4"]) };
        return (script, BattleIntro.Plan(battle, script.Stage, script.Terrain, walkers), walkers, slots);
    }

    /// <summary>Where each hero is drawn <paramref name="t"/> seconds in, in the stage's tiles: walking, or on its slot.</summary>
    private static Dictionary<UnitId, Vector2> Where(BattleIntro intro, BoardTerrain terrain, Tile[] slots, double t)
    {
        var frame = intro.At(t, terrain);
        return intro.Heroes.Select((h, i) => (h.Unit, At: frame.Places.TryGetValue(h.Unit, out var place) ? place.Map
            : new Vector2(slots[int.Parse(h.Unit.Value[4..], System.Globalization.CultureInfo.InvariantCulture) - 1].X + 0.5f,
                slots[int.Parse(h.Unit.Value[4..], System.Globalization.CultureInfo.InvariantCulture) - 1].Y + 0.5f)))
            .ToDictionary(p => p.Unit, p => p.At);
    }

    // The hardware check (local-windows-checks §5.6): four heroes walked in and three bodies showed, two bars stacked on one spot. The
    // property: whatever the ground and wherever the walkers stand, two heroes are never closer than half a tile, from the first step on.
    // Walkers that stood together may share their tile until then.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Two_heroes_are_never_on_one_tile_in_the_walk_in(int seed)
    {
        var (script, intro, walkers, slots) = Bunched(seed);
        if (intro.Fades)
            return;     // put on their slots in the dark

        foreach (var walk in intro.Heroes)
        {
            var from = walkers[walk.Walker];
            Assert.Equal(new Tile((int)MathF.Floor(from.Map.X), (int)MathF.Floor(from.Map.Y)), walk.Path[0]);
            Assert.All(walk.Path.Zip(walk.Path.Skip(1)), p => Assert.True(p.First == p.Second || WorldWalk.Legal(script.Terrain.Rules, p.First, p.Second, ClassDef.DefaultJump)));
            Assert.True(walk.Steps < StageTuning.FarSteps);
        }
        Assert.True(intro.WalkEnd <= StageTuning.WalkInMost + 1e-9);
        for (var t = 0.0; t <= intro.WalkEnd + 0.01; t += 0.004)
        {
            var at = Where(intro, script.Terrain, slots, t).ToArray();
            for (var a = 0; a < at.Length; a++)
                for (var b = a + 1; b < at.Length; b++)
                {
                    var together = intro.Heroes.Single(h => h.Unit == at[a].Key).Path[0] == intro.Heroes.Single(h => h.Unit == at[b].Key).Path[0];
                    if (together && t < intro.Step)
                        continue;
                    Assert.True(Vector2.Distance(at[a].Value, at[b].Value) >= 0.5f, $"{at[a].Key} and {at[b].Key} at {t:0.000} s: {at[a].Value}, {at[b].Value}");
                }
        }
    }

    // That the property isn't empty: most of these parties walk in rather than fade, and the shortest walks alone would have run into each
    // other in many of them (47 of 60 when written), which is what the timing is for. All 60 walked.
    [Fact]
    public void Bunched_parties_mostly_walk_and_their_shortest_walks_alone_would_collide()
    {
        var (walked, collided) = (0, 0);
        foreach (var seed in Enumerable.Range(1, 60))
        {
            var (script, intro, walkers, slots) = Bunched(seed);
            if (intro.Fades)
                continue;
            walked++;
            var shortest = intro.Heroes.Select(h => BattleIntro.PathTo(script.Terrain.Rules, h.Path[0], h.Path[^1])).ToArray();
            var longest = shortest.Max(p => p.Length);
            for (var k = 1; k < longest; k++)
            {
                var on = shortest.Select(p => p[Math.Min(k, p.Length - 1)]).ToArray();
                if (on.Distinct().Count() < on.Length)
                {
                    collided++;
                    break;
                }
            }
        }
        Assert.True(walked >= 50, $"{walked} of 60 walked");
        Assert.True(collided >= 20, $"{collided} would have collided");
    }

    [Fact]
    public void A_hero_on_its_slot_stays_and_the_others_go_round()
    {
        // hero2 stands on its slot, which is on hero1's way: hero2 stays, and hero1 goes round it.
        var script = new StageScript();
        ImmutableArray<UnitView> units = [Unit("hero1", "Party", new Tile(2, 2)), Unit("hero2", "Party", new Tile(1, 2)), Unit("enemy1", "Enemy", new Tile(7, 0))];
        var walkers = new Dictionary<string, UnitPlace>
        {
            ["p1"] = new(new Vector2(Origin.X - 0.5f, Origin.Y + 2.5f), 0),
            ["p2"] = new(new Vector2(Origin.X + 1.5f, Origin.Y + 2.5f), 0),
        };
        var intro = BattleIntro.Plan(Battle(units: units), script.Stage, script.Terrain, walkers);

        Assert.False(intro.Fades);
        var hero1 = intro.Heroes.Single(h => h.Unit.Value == "hero1");
        Assert.DoesNotContain(new Tile(Origin.X + 1, Origin.Y + 2), hero1.Path);
        Assert.Equal(new Tile(Origin.X + 2, Origin.Y + 2), hero1.Path[^1]);
        Assert.Equal(0, intro.Heroes.Single(h => h.Unit.Value == "hero2").Steps);
    }

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
