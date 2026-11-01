using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="BeatLoop"/>: what the TV does each frame, so that nothing failing can stick it on a beat or keep Results from showing.</summary>
public sealed class BeatLoopTests
{
    private const double Frame = 1 / 60.0;

    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly UnitId Hero = new("hero1");

    private static BattleView View(int x, params Beat[] beats)
        => new("e", "river_crossing", 1, [], null, [new UnitView(Hero, "Hero", "Party", "characters.class_fighter", new Tile(x, 3), 10, 10, 0, "Up", 0, [], null)],
            [.. beats], null, BoardChanges.None);

    private static Beat Walk(int seq, int from, int to) => new(seq, Hero, [new Moved(Hero, [new Tile(from, 3), new Tile(to, 3)])]);

    private static Beat Broken(int seq) => new(seq, Hero, [new Said(Hero.Value, null!)]);

    private sealed class Rig
    {
        public Rig(Action<BeatTick>? present = null)
        {
            Player = new BeatPlayer(Fx.Value, Art.Value);
            Loop = new BeatLoop(Player, Shown.Add, present ?? (_ => { }), Logged.Add);
        }

        public BeatPlayer Player { get; }
        public BeatLoop Loop { get; }
        public List<int> Shown { get; } = [];
        public List<string> Logged { get; } = [];

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += Frame)
                Loop.Step(Frame);
        }
    }

    private static Action<BeatTick> KeepsThrowing() => _ => throw new InvalidOperationException("no drawing today");

    [Fact]
    public void A_redraw_that_keeps_throwing_still_reaches_results()
    {
        var rig = new Rig(KeepsThrowing());
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(6, Walk(1, 3, 4), Walk(2, 4, 5), Walk(3, 5, 6)));
        var results = 0;
        rig.Loop.Held = () => results++;

        rig.Run(3);

        Assert.True(rig.Player.Idle);
        Assert.Equal(1, results);
        Assert.Equal([1, 2, 3], rig.Shown);
    }

    [Fact]
    public void A_redraw_that_keeps_throwing_is_logged_once()
    {
        var rig = new Rig(KeepsThrowing());
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(5, Walk(1, 3, 4), Walk(2, 4, 5)));

        rig.Run(3);

        Assert.Single(rig.Logged);
    }

    [Fact]
    public void A_failing_redraw_does_not_drop_the_innocent_beat_it_happened_during()
    {
        var rig = new Rig(KeepsThrowing());
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(4, Walk(1, 3, 4)));

        rig.Run(1);

        Assert.Equal(new Tile(4, 3), rig.Player.Shown!.Units[0].At);
        Assert.Equal([1], rig.Shown);
        Assert.DoesNotContain(rig.Logged, l => l.Contains("beat", StringComparison.Ordinal));
    }

    [Fact]
    public void A_beat_that_throws_is_dropped_reported_and_logged_once()
    {
        var rig = new Rig();
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(4, Broken(1), Walk(2, 3, 4)));
        var results = 0;
        rig.Loop.Held = () => results++;

        rig.Run(2);

        Assert.Equal([1, 2], rig.Shown);
        Assert.Single(rig.Logged);
        Assert.Contains("beat 1", rig.Logged[0], StringComparison.Ordinal);
        Assert.Equal(1, results);
    }

    [Fact]
    public void Held_waits_for_the_last_beat()
    {
        var rig = new Rig();
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(4, Walk(1, 3, 4)));
        var results = 0;
        rig.Loop.Held = () => results++;

        rig.Loop.Step(Frame);
        Assert.Equal(0, results);

        rig.Run(1);
        Assert.Equal(1, results);
    }

    [Fact]
    public void What_is_held_can_be_taken_back()
    {
        var rig = new Rig();
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(4, Walk(1, 3, 4)));
        var results = 0;
        rig.Loop.Held = () => results++;

        rig.Loop.Held = null;
        rig.Run(1);

        Assert.Equal(0, results);
    }

    [Fact]
    public void A_different_failure_is_logged_too()
    {
        var failures = 0;
        var rig = new Rig(_ => throw new InvalidOperationException($"failure {failures++ / 10}"));
        rig.Player.Receive(View(3));
        rig.Player.Receive(View(5, Walk(1, 3, 4), Walk(2, 4, 5)));

        rig.Run(0.5);

        Assert.True(rig.Logged.Count > 1, $"{rig.Logged.Count}");
    }

    [Fact]
    public void A_guarded_draw_outside_the_loop_goes_the_same_way()
    {
        var rig = new Rig();

        rig.Loop.Guard(() => throw new InvalidOperationException("no drawing today"));
        rig.Loop.Guard(() => throw new InvalidOperationException("no drawing today"));

        Assert.Single(rig.Logged);
    }

    [Fact]
    public void Held_results_that_fail_to_show_are_logged_and_the_tv_goes_on()
    {
        var rig = new Rig();
        rig.Player.Receive(View(3));
        rig.Loop.Held = () => throw new InvalidOperationException("a bad results view");

        rig.Loop.Step(Frame);
        rig.Loop.Step(Frame);

        Assert.Null(rig.Loop.Held);
        Assert.Contains("a bad results view", Assert.Single(rig.Logged), StringComparison.Ordinal);
    }

    [Fact]
    public void Held_results_wait_out_a_catch_up_and_then_show()
    {
        var rig = new Rig();
        rig.Player.Receive(View(1));
        rig.Player.Receive(View(1, Walk(1, 1, 2)));
        rig.Loop.Step(Frame);
        rig.Player.Receive(View(31, Walk(29, 29, 30), Walk(30, 30, 31)));
        var results = 0;
        rig.Loop.Held = () => results++;

        rig.Run(3);

        Assert.Equal([1, 30], rig.Shown);
        Assert.Equal(1, results);
    }
}
