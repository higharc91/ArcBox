using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WalkShow"/>: a frame of the party on the TV, as the scene draws it.</summary>
public sealed class WalkShowTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<SoundBank> Sounds = new(() => SoundBank.Load(Pack.Root));
    private static readonly BoardTerrain Flat = Ground(160, 120);
    private static readonly Tile Start = new(80, 60);

    private static WalkShow Show(BoardTerrain? terrain = null) => new(terrain ?? Flat, Art.Value, Sounds.Value);

    /// <summary>Walks east in real time and counts the footsteps.</summary>
    private static int Footsteps(WalkShow show, Script script, int steps, int viewsPerStep = 1)
    {
        var count = 0;
        for (var i = 0; i < steps; i++)
        {
            var ms = script.Step(1, 0);
            for (var view = 0; view < viewsPerStep; view++)
                show.Receive(script.Now(true));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
                count += show.Frame(Frame).Footstep is null ? 0 : 1;
        }
        return count;
    }

    [Fact]
    public void A_footstep_goes_by_the_distance_walked()
    {
        var show = Show();
        var script = new Script(Start);
        show.Receive(script.Now(false));
        show.Frame(Frame);

        var heard = Footsteps(show, script, 22);

        var expected = (int)(22 / WorldTuning.FootstepTiles);
        Assert.InRange(heard, expected - 2, expected + 1);
    }

    [Fact]
    public void More_views_dont_mean_more_footsteps()
    {
        var (one, many) = (Show(), Show());
        var (first, second) = (new Script(Start), new Script(Start));
        one.Receive(first.Now(false));
        many.Receive(second.Now(false));

        Assert.Equal(Footsteps(one, first, 12), Footsteps(many, second, 12, viewsPerStep: 5));
    }

    [Fact]
    public void Standing_still_makes_no_sound()
    {
        var show = Show();
        show.Receive(View(Start));

        for (var i = 0; i < 500; i++)
        {
            show.Receive(View(Start));
            Assert.Null(show.Frame(Frame).Footstep);
        }
    }

    [Fact]
    public void A_snap_makes_no_footsteps()
    {
        var show = Show();
        var script = new Script(Start);
        show.Receive(script.Now(false));
        show.Frame(Frame);
        for (var i = 0; i < 40; i++)
            script.Step(1, 0);

        show.Receive(script.Now(false));        // far past what the path can reach: every walker is put there
        var frames = Enumerable.Range(0, 30).Select(_ => show.Frame(Frame)).ToArray();

        Assert.All(frames, f => Assert.Null(f.Footstep));
    }

    [Fact]
    public void A_footstep_sounds_like_the_ground_underfoot()
    {
        var ground = Pack.Ground("ashen_ford");
        var terrain = new BoardTerrain(ground.Map, null);
        bool Cobble(Tile t) => terrain.Map.InBounds(t) && terrain.Map.Materials[terrain.Map[t].Material].StartsWith("cobble", StringComparison.Ordinal);
        var road = Enumerable.Range(0, terrain.Map.Width).SelectMany(x => Enumerable.Range(0, terrain.Map.Height).Select(y => new Tile(x, y)))
            .First(t => Enumerable.Range(0, 8).All(i => Cobble(new Tile(t.X + i, t.Y))));
        var show = Show(terrain);
        var script = new Script(road);
        show.Receive(script.Now(false));

        var heard = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var ms = script.Step(1, 0);
            show.Receive(script.Now(true));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
                if (show.Frame(Frame).Footstep is { } step)
                    heard.Add(step);
        }

        Assert.NotEmpty(heard);
        Assert.All(heard, step => Assert.Equal("step_stone", step));
    }

    [Fact]
    public void A_new_epoch_cuts_the_camera_to_the_party()
    {
        var show = Show();
        show.Receive(View(Start));
        show.Frame(Frame);
        var at = new Tile(30, 30);

        show.Receive(View(at, epoch: 2));
        var frame = show.Frame(Frame);

        Assert.True(frame.Cut);
        var leader = frame.Units.Single(u => u.CrownAt is not null);
        var screen = frame.Fit.ToScreen(leader.Feet);
        Assert.InRange(screen.X, 940, 980);
        Assert.InRange(screen.Y, 540, 600);
        Assert.False(show.Frame(Frame).Cut);
    }

    [Fact]
    public void A_tv_joining_mid_stride_starts_on_the_party_with_followers_on_the_trail()
    {
        var script = new Script(Start, party: 3);
        for (var i = 0; i < 6; i++)
            script.Step(1, 0);
        var show = Show();

        show.Receive(script.Now(true));
        var frame = show.Frame(Frame);

        Assert.True(frame.Cut);
        Assert.Equal(3, frame.Units.Length);
        Assert.Equal(3, frame.Units.Select(u => u.Feet).Distinct().Count());       // nobody stacked on the leader
    }

    [Fact]
    public void Each_step_is_told_once_as_it_arrives()
    {
        var show = Show();
        var script = new Script(Start);

        Assert.Empty(show.Receive(script.Now(false)));
        script.Step(1, 0);
        script.Step(1, 0);
        Assert.Equal([1, 2], show.Receive(script.Now(true)).Select(l => l.Seq));
        Assert.Empty(show.Receive(script.Now(true)));
        script.Step(1, 0);
        Assert.Equal([3], show.Receive(script.Now(true)).Select(l => l.Seq));
    }

    [Fact]
    public void A_stale_view_changes_nothing()
    {
        var show = Show();
        var script = new Script(Start, party: 3);
        script.Step(1, 0);
        var early = script.Now(true);
        script.Step(1, 0);
        show.Receive(script.Now(true));

        Assert.Empty(show.Receive(early with { Party = Party(1) }));

        Assert.Equal(3, show.Frame(Frame).Units.Length);
    }

    [Fact]
    public void Someone_whose_phone_is_gone_is_dimmed_and_keeps_their_place()
    {
        var show = Show();
        var view = View(Start);
        var gone = view with { Party = [.. view.Party.Select(w => w.Id == "b" ? w with { Here = false } : w)] };

        show.Receive(gone);
        var units = show.Frame(Frame).Units;

        Assert.Equal(["b"], units.Where(u => u.Away).Select(u => u.Id.Value));
        Assert.Equal(3, units.Length);
    }

    [Fact]
    public void The_party_says_where_to_load_ahead()
    {
        var show = Show();
        var script = new Script(Start);
        show.Receive(script.Now(false));
        var frame = show.Frame(Frame);
        Assert.Equal(Vector2.Zero, frame.Velocity);

        for (var i = 0; i < 12; i++)
        {
            var ms = script.Step(1, 0);
            show.Receive(script.Now(true));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
                frame = show.Frame(Frame);
        }

        Assert.InRange(frame.Velocity.X, 3, 3.5f);
        Assert.InRange(frame.Tile.X, Start.X + 10, Start.X + 12.6f);
    }

    /// <summary>
    /// What was flagged: the camera rounds to whole pixels and the feet don't, so at a walk's beat the two rounded differently and the
    /// party shook against the frame by a pixel. On screen, the feet land where the unrounded camera puts them.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    public void The_leader_doesnt_shake_on_screen_on_a_steady_walk(int dx, int dy)
    {
        var show = Show();
        var script = new Script(Start, party: 1);
        show.Receive(script.Now(false));
        var seen = new List<Vector2>();
        for (var i = 0; i < 30; i++)
        {
            var ms = script.Step(dx, dy);
            show.Receive(script.Now(true));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
            {
                var frame = show.Frame(Frame);
                seen.Add(frame.Fit.ToScreen(frame.Units[0].Feet));
            }
        }

        var steady = seen.Skip(seen.Count / 2).ToArray();
        Assert.All(steady, p =>
        {
            Assert.Equal(MathF.Round(p.X), p.X, 3);
            Assert.Equal(MathF.Round(p.Y), p.Y, 3);
        });
        Assert.InRange(Reversals(steady.Select(p => p.X)), 0, 40);       // the walk's own rate wobble, and never a pixel a frame
        Assert.InRange(Reversals(steady.Select(p => p.Y)), 0, 40);
    }

    /// <summary>The camera stops at its margin and the party doesn't, so this is how far the party can get from the middle of the view.</summary>
    [Theory]
    [InlineData(159, 63)]
    [InlineData(0, 60)]
    [InlineData(80, 0)]
    [InlineData(80, 119)]
    [InlineData(159, 0)]
    [InlineData(159, 119)]
    [InlineData(0, 119)]
    [InlineData(0, 0)]
    public void The_party_is_on_screen_at_the_edge_of_a_region(int x, int y)
    {
        var show = Show();
        show.Receive(View(new Tile(x, y)));

        var frame = show.Frame(Frame);
        for (var t = 0.0; t < 3; t += Frame)
            frame = show.Frame(Frame);

        var feet = frame.Fit.ToScreen(frame.Units.Single(u => u.CrownAt is not null).Feet);
        Assert.InRange(feet.X, 0, 1920);
        Assert.InRange(feet.Y, 0, 1080);
    }

    /// <summary>However a walk goes, doubling back, with views lost and steps bunched, the party is together again at the next rest.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void After_any_walk_the_party_gathers_round_the_leader(int seed)
    {
        var random = new Random(seed);
        var show = Show();
        var script = new Script(Start, party: 3);
        show.Receive(script.Now(false));
        (int, int)[] ways = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)];
        for (var rest = 0; rest < 30; rest++)
        {
            var way = ways[random.Next(ways.Length)];
            for (var step = random.Next(1, 12); step > 0; step--)
            {
                if (random.Next(5) == 0)
                    way = random.Next(2) == 0 ? (-way.Item1, -way.Item2) : ways[random.Next(ways.Length)];
                var ms = script.Step(way.Item1, way.Item2);
                if (random.Next(4) != 0)        // a lost view: the next one's legs fill it in
                    show.Receive(script.Now(true));
                for (var t = 0.0; t < ms / 1000.0; t += 1.0 / 60)
                    show.Frame(1.0 / 60);
            }
            show.Receive(script.Now(false));
            var frame = show.Frame(1.0 / 60);
            for (var t = 0.0; t < 4; t += 1.0 / 60)
                frame = show.Frame(1.0 / 60);

            var leader = frame.Units.Single(u => u.CrownAt is not null).Feet;
            Assert.All(frame.Units, u => Assert.InRange(Vector2.Distance(u.Feet, leader), 0, 240));     // art pixels: under four tiles along a diagonal
        }
    }

    [Fact]
    public void A_party_at_rest_keeps_still_against_the_ground_as_the_camera_settles()
    {
        var show = Show();
        var script = new Script(Start, party: 3);
        show.Receive(script.Now(false));
        for (var i = 0; i < 10; i++)
        {
            var ms = script.Step(1, 0);
            show.Receive(script.Now(i < 9));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
                show.Frame(Frame);
        }
        for (var t = 0.0; t < 2; t += Frame)      // the fan-out takes its time
            show.Frame(Frame);

        var settled = show.Frame(Frame).Units.ToDictionary(u => u.Id, u => u.Feet);
        for (var t = 0.0; t < 3; t += Frame)
            Assert.All(show.Frame(Frame).Units, u => Assert.Equal(settled[u.Id], u.Feet));
    }

    // Interaction review 5b, item 2: a press into a wall leans the leader into it and back, with a knock, and nobody else moves.
    [Fact]
    public void A_wall_bumps_the_leader_alone_and_knocks_once()
    {
        var (show, script, still, drawn) = Bumpable();

        show.Receive(Bumped(script, 5));
        var frames = new List<WalkFrame>();
        var leaned = 0f;
        for (var t = 0.0; t < WorldTuning.BumpSeconds + 0.1; t += Frame)
        {
            var frame = show.Frame(Frame);
            frames.Add(frame);
            var feet = frame.Units.ToDictionary(u => u.Id.Value, u => u.Feet);
            leaned = Math.Max(leaned, Vector2.Distance(drawn["a"], feet["a"]));
            Assert.Equal(drawn["b"], feet["b"]);
            Assert.Equal(drawn["c"], feet["c"]);
            Assert.Equal(still, show.Places.ToDictionary(p => p.Key, p => p.Value.Map));     // what's logged is where the track put them
        }

        Assert.Single(frames, f => f.Bumped);
        Assert.NotNull(frames.Single(f => f.Bumped).Bump);
        Assert.True(leaned >= 2, $"the leader leaned {leaned} px");
        Assert.Equal(drawn["a"], frames[^1].Units.Single(u => u.Id.Value == "a").Feet);      // and back
    }

    // Review: the knock is heard only with a lean to see. Under the title card, while the world opens, there's neither.
    [Fact]
    public void A_bump_under_the_title_card_is_neither_seen_nor_heard()
    {
        var (show, script, _, drawn) = Bumpable();

        show.Receive(Bumped(script, 5));
        for (var t = 0.0; t < WorldTuning.BumpSeconds + 0.1; t += Frame)
        {
            var frame = show.Frame(Frame, revealed: false);
            Assert.False(frame.Bumped);
            Assert.Null(frame.Bump);
            Assert.Equal(drawn["a"], frame.Units.Single(u => u.Id.Value == "a").Feet);
        }
        Assert.All(Enumerable.Range(0, 30).Select(_ => show.Frame(Frame)), f => Assert.False(f.Bumped));      // nor later, once it's up
    }

    [Fact]
    public void A_bump_with_nobody_leading_is_neither_seen_nor_heard()
    {
        var (show, script, _, _) = Bumpable();

        var view = Bumped(script, 5);
        show.Receive(view with { Party = [.. view.Party.Select(w => w with { Leads = false })], Leader = null });

        Assert.All(Enumerable.Range(0, 30).Select(_ => show.Frame(Frame)), f => Assert.False(f.Bumped));
    }

    [Fact]
    public void After_a_reconnect_an_old_count_doesnt_bump()
    {
        var (show, script, _, _) = Bumpable();

        show.Rebase();
        show.Receive(Bumped(script, 9));

        Assert.All(Enumerable.Range(0, 30).Select(_ => show.Frame(Frame)), f => Assert.False(f.Bumped));
    }

    /// <summary>A party of three at rest, with a count of refused steps already in: where the track has them, and where they're drawn.</summary>
    private static (WalkShow Show, Script Script, Dictionary<string, Vector2> Still, Dictionary<string, Vector2> Drawn) Bumpable()
    {
        var show = Show();
        var script = new Script(Start, 3);
        show.Receive(script.Now(false) with { Bumps = 4 });
        WalkFrame frame = null!;
        for (var t = 0.0; t < 2; t += Frame)
        {
            frame = show.Frame(Frame);
            Assert.False(frame.Bumped);         // a first view's count is old news
        }
        return (show, script, show.Places.ToDictionary(p => p.Key, p => p.Value.Map), frame.Units.ToDictionary(u => u.Id.Value, u => u.Feet));
    }

    private static ExploreView Bumped(Script script, int bumps)
        => script.Now(false) with { Facing = new Tile(script.At.X, script.At.Y - 1), Blocked = true, Bumps = bumps };

    private static int Reversals(IEnumerable<float> values)
    {
        var (count, last) = (0, 0);
        float? before = null;
        foreach (var value in values)
        {
            if (before is { } was && value != was)
            {
                var way = Math.Sign(value - was);
                if (last != 0 && way != last)
                    count++;
                last = way;
            }
            before = value;
        }
        return count;
    }
}
