using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>The party on the move: the press, the step, the lease, the lead, and saving as it goes.</summary>
public sealed class WalkingTests
{
    private const string Step = TacticsGame.Timers.Step;
    private const string Lead = TacticsGame.Timers.Lead;
    private const string Save = TacticsGame.Timers.Save;

    private static readonly TimeSpan Ms100 = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Ms300 = TimeSpan.FromMilliseconds(300);

    private static Terrain Ground => Rules.GroundFor("ashen_ford")!.Terrain;

    private static WorldRegion Region => Rules.RegionFor("ashen_ford")!;

    private static HashSet<Tile> Reached => WorldWalk.Reach(Ground, Region.Def.Spawns.Start, ClassDef.DefaultJump);

    private static (WalkDir Dir, string Name)[] Dirs { get; } =
        [.. Enum.GetValues<WalkDir>().Select(d => (d, d.ToString().ToLowerInvariant()))];

    /// <summary>A tile with <paramref name="length"/> plain, level steps ahead of it to the screen's south-east.</summary>
    private static Tile Road(int length = 30)
    {
        foreach (var tile in Reached.Order())
        {
            var at = tile;
            var steps = 0;
            while (steps < length && WorldWalk.Next(Ground, at, WalkDir.SE, ClassDef.DefaultJump, null) is { Cost: 1, Slid: false } move
                   && Ground.HeightAt(move.To) == Ground.HeightAt(at))
            {
                at = move.To;
                steps++;
            }
            if (steps == length)
                return tile;
        }
        throw new InvalidOperationException("Ashen Ford has no straight road that long.");
    }

    /// <summary>A session already in the world at <paramref name="at"/>, everyone here, with player 0 leading.</summary>
    private static Session At(Tile at)
    {
        var s = new Session(document: DocumentAt(Saved(at)));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
        return s;
    }

    private static void Walk(Session s, string? dir, int player = 0) => s.Send(player, "walk", new { dir });

    private static ExploreView Party(Session s) => s.Host.Explore!;

    private static string PhoneJson(Session s, int player) => JsonSerializer.Serialize(s.Harness.PlayerView(player), ArcJson.Options);

    private static string HostJson(Session s) => JsonSerializer.Serialize(s.Game.HostView(), ArcJson.Options);

    /// <summary>Holds <paramref name="dir"/>, pinging as a phone does, until the party has taken <paramref name="steps"/> more steps.</summary>
    private static void Hold(Session s, string dir, int steps)
    {
        var goal = Party(s).Seq + steps;
        Walk(s, dir);
        for (var i = 0; i < 100 * steps && Party(s).Seq < goal; i++)
        {
            s.Harness.Advance(Ms100);
            Walk(s, dir);
        }
        Assert.Equal(goal, Party(s).Seq);
    }

    // ---- The press ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_press_steps_at_once()
    {
        var road = Road();
        var s = At(road);

        Walk(s, "se");     // no time passes

        Assert.Equal(new Tile(road.X + 1, road.Y), Party(s).At);
        Assert.Equal(1, Party(s).Seq);
        Assert.True(Party(s).Held);
        Assert.Equal(new WalkLeg(1, new Tile(road.X + 1, road.Y), 300), Assert.Single(Party(s).Legs));
    }

    [Fact]
    public void Holding_steps_at_the_rate()
    {
        var s = At(Road());

        Walk(s, "se");
        for (var i = 0; i < 12; i++)
        {
            s.Harness.Advance(TimeSpan.FromMilliseconds(250));
            Walk(s, "se");
        }

        Assert.Equal(11, Party(s).Seq);    // at 0 ms, then one every 300 ms up to 3000 ms
    }

    [Fact]
    public void Diagonals_and_difficult_ground_take_longer()
    {
        var road = Reached.Order().First(t => WorldWalk.Next(Ground, t, WalkDir.E, ClassDef.DefaultJump, null) is { Cost: 1, Diagonal: true });
        var s = At(road);
        Walk(s, "e");
        Assert.Equal(424, Party(s).Legs[^1].Ms);   // 300 × √2

        var ford = Reached.Order().First(t => WorldWalk.Next(Ground, t, WalkDir.SE, ClassDef.DefaultJump, null) is { Cost: 2 });
        s = At(ford);
        Walk(s, "se");
        Assert.Equal(600, Party(s).Legs[^1].Ms);
    }

    [Fact]
    public void A_long_drop_takes_longer()
    {
        // Ashen Ford has no drop this long yet, so paint a stair up to one: two climbs of 2, and a drop of 4 back down.
        var ground = OpenGround();
        var (stair, top) = (ground, new Tile(ground.X + 1, ground.Y));
        var height = Ground.HeightAt(ground);
        var cliffed = new TacticsGame(Painted((stair, 0, (byte)(height + 2)), (top, 0, (byte)(height + 4))));
        var s = new Session(document: DocumentAt(Saved(top)), game: cliffed);
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(top, Party(s).At);

        Walk(s, "se");

        Assert.Equal(300 + 60 * (4 - 2), Party(s).Legs[^1].Ms);
    }

    [Fact]
    public void Pings_dont_change_the_speed()
    {
        var s = At(Road());

        Walk(s, "se");
        for (var i = 0; i < 46; i++)      // 15 a second for 3 seconds, and one more for the rounding
        {
            s.Harness.Advance(TimeSpan.FromMilliseconds(1000.0 / 15));
            Walk(s, "se");
        }

        Assert.Equal(11, Party(s).Seq);
    }

    [Fact]
    public void A_tap_moves_one_tile()
    {
        var road = Road();
        var s = At(road);

        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromMilliseconds(40));
        Walk(s, null);
        s.Harness.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(new Tile(road.X + 1, road.Y), Party(s).At);
        Assert.False(Party(s).Held);
    }

    [Fact]
    public void Tapping_cant_outrun_the_walk()
    {
        var s = At(Road());

        for (var i = 0; i < 10; i++)      // a thumb drumming at 40 Hz
        {
            Walk(s, "se");
            s.Harness.Advance(TimeSpan.FromMilliseconds(12));
            Walk(s, null);
            s.Harness.Advance(TimeSpan.FromMilliseconds(13));
        }

        Assert.True(Party(s).Seq <= 1, $"{Party(s).Seq} steps in 250 ms");
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_press_in_the_middle_of_a_step_goes_when_that_step_ends()
    {
        var s = At(Road());
        Walk(s, "se");
        s.Harness.Advance(Ms100);
        Walk(s, null);

        s.Harness.Advance(Ms100);
        Walk(s, "se");
        Assert.Equal(1, Party(s).Seq);
        Assert.True(Party(s).Held);

        s.Harness.Advance(Ms100);
        Assert.Equal(2, Party(s).Seq);
    }

    [Fact]
    public void A_new_direction_takes_effect_at_the_next_tile()
    {
        var road = Road();
        var s = At(road);
        Walk(s, "se");

        s.Harness.Advance(Ms100);
        Walk(s, "nw");
        Assert.Equal(1, Party(s).Seq);

        s.Harness.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(road, Party(s).At);
        Assert.Equal(new Tile(road.X - 1, road.Y), Party(s).Facing);
    }

    [Fact]
    public void A_stop_ends_the_timer()
    {
        var s = At(Road());
        Walk(s, "se");
        Assert.Contains(Step, s.Harness.ActiveTimers);

        Walk(s, null);

        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.False(Party(s).Held);
    }

    [Fact]
    public void A_stop_is_safe_to_send_twice()
    {
        var s = At(Road());
        Walk(s, null);
        Walk(s, "se");
        Walk(s, null);
        var view = HostJson(s);

        Walk(s, null);

        Assert.Equal(view, HostJson(s));
    }

    // ---- The lease ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_lost_release_stops_within_the_lease()
    {
        var s = At(Road());

        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromMilliseconds(749));
        Assert.True(Party(s).Held);
        var seq = Party(s).Seq;
        s.Harness.Advance(TimeSpan.FromMilliseconds(1));

        Assert.False(Party(s).Held);
        Assert.Equal(3, seq);       // 0, 300 and 600 ms: the worst run-on is about 3 tiles
        s.Harness.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(seq, Party(s).Seq);
    }

    [Fact]
    public void No_timer_is_left_after_the_lease()
    {
        var s = At(Road());

        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.Equal([Lead], s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_stop_that_arrives_before_its_press_is_ended_by_the_lease()
    {
        var s = At(Road());

        Walk(s, null);       // reordered: the release gets here first
        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromMilliseconds(750));

        Assert.False(Party(s).Held);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_ping_after_a_lease_ran_out_picks_the_walk_back_up()
    {
        var s = At(Road());
        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromSeconds(2));   // a network blip
        var seq = Party(s).Seq;

        Walk(s, "se");

        Assert.Equal(seq + 1, Party(s).Seq);
    }

    [Fact]
    public void A_late_timer_doesnt_drift()
    {
        var c = new Clock(At(Road()));
        c.Walk("se");                                 // the step runs to 300 ms

        c.Pass(330);
        c.Walk("se");                                 // lease
        c.Fire(Step);

        Assert.Equal(2, c.Seq);
        Assert.Equal(c.Started + TimeSpan.FromMilliseconds(600), c.Deadline(Step));   // not 630
    }

    [Fact]
    public void A_long_stall_gives_one_step_not_a_burst()
    {
        var c = new Clock(At(Road()));
        c.Walk("se");

        c.Pass(5000);
        c.Walk("se");
        c.Fire(Step);

        Assert.Equal(2, c.Seq);
        Assert.Equal(c.Started + TimeSpan.FromMilliseconds(5300), c.Deadline(Step));
    }

    [Fact]
    public void A_step_timer_after_a_stop_does_nothing()
    {
        var c = new Clock(At(Road()));
        c.Walk("se");
        c.Walk(null);

        c.Pass(1000);
        c.Fire(Step);

        Assert.Equal(1, c.Seq);
    }

    [Fact]
    public void A_walking_timer_outside_exploration_does_nothing()
    {
        var c = new Clock(At(Road()));
        c.Walk("se");
        c.Session(0, "battles");

        c.Fire(Step);
        c.Fire(Lead);
        c.Fire(Save);

        Assert.Equal(1, c.Seq);
        Assert.Equal(TacticsPhase.EncounterPick, c.Game.Phase);
    }

    // ---- Who can steer ------------------------------------------------------------------------------------------------

    [Fact]
    public void Someone_else_cant_steer()
    {
        var road = Road();
        var s = At(road);

        Walk(s, "se", player: 1);
        Assert.Equal(road, Party(s).At);
        Assert.False(Party(s).Held);

        Walk(s, "se");
        Walk(s, null, player: 1);       // nor stop the party
        Assert.True(Party(s).Held);
    }

    [Theory]
    [InlineData("""{ "dir": "up" }""")]
    [InlineData("""{ "dir": "NE" }""")]
    [InlineData("""{ "dir": "1" }""")]         // Enum.Parse would take this
    [InlineData("""{ "dir": 2 }""")]
    [InlineData("""{ "dir": ["e"] }""")]
    [InlineData("""{ }""")]
    [InlineData("""7""")]
    [InlineData("""null""")]
    public void A_bad_direction_is_ignored(string data)
    {
        var road = Road();
        var s = At(road);
        var input = JsonDocument.Parse($$"""{ "action": "walk", "data": {{data}} }""").RootElement;
        var phone = PhoneJson(s, 0);

        s.Harness.Input(0, input);

        Assert.Equal(road, Party(s).At);
        Assert.False(Party(s).Held);
        Assert.Equal(phone, PhoneJson(s, 0));

        // Nor does it stop a walk, or keep one going.
        Walk(s, "se");
        s.Harness.Advance(TimeSpan.FromMilliseconds(700));
        s.Harness.Input(0, input);
        Assert.True(Party(s).Held);
        s.Harness.Advance(TimeSpan.FromMilliseconds(50));
        Assert.False(Party(s).Held);
    }

    [Fact]
    public void A_battle_input_doesnt_reach_walking()
    {
        var s = new Session(seed: 3);
        s.ToBattle();
        for (var p = 0; p < 4; p++)
            Walk(s, "ne", p);

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        for (var p = 0; p < 4; p++)
            Assert.Null(s.Screen(p)?.Header);   // BattleInput would have toasted the player whose turn it is
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_walk_at_the_battle_list_or_results_is_ignored()
    {
        var s = At(Road());
        s.Send(0, "battles");
        Walk(s, "se");
        Assert.Equal(0, Party(s).Seq);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.Null(s.Screen(0)?.Header);

        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);
        s.PlayOut();
        s.Harness.SetConnected(0, true);
        Walk(s, "se");

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        Assert.False(Party(s).Held);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
    }

    // ---- Walls --------------------------------------------------------------------------------------------------------

    private static (Tile At, string Dir, WalkDir Blocked) Wall()
    {
        foreach (var tile in Reached.Order())
            foreach (var dir in new[] { WalkDir.SE, WalkDir.NW, WalkDir.NE, WalkDir.SW })
                if (WorldWalk.Next(Ground, tile, dir, ClassDef.DefaultJump, null) is null)
                    return (tile, dir.ToString().ToLowerInvariant(), dir);
        throw new InvalidOperationException("Ashen Ford has no walls.");
    }

    [Fact]
    public void A_blocked_step_turns_the_party_and_keeps_it_there_without_a_word()
    {
        var (at, dir, blocked) = Wall();
        var s = At(at);
        var (dx, dy) = WorldWalk.Delta(blocked);
        var phone = PhoneJson(s, 0);

        Walk(s, dir);

        Assert.Equal(at, Party(s).At);
        Assert.Equal(new Tile(at.X + dx, at.Y + dy), Party(s).Facing);
        Assert.Empty(Party(s).Legs);
        Assert.True(Party(s).Held);
        Assert.Equal(phone, PhoneJson(s, 0));
    }

    [Fact]
    public void A_wall_ticks_only_for_the_lease()
    {
        var (at, dir, _) = Wall();
        var c = new Clock(At(at));

        c.Walk(dir);

        Assert.Equal(c.Started + TimeSpan.FromMilliseconds(750), c.Deadline(Step));
    }

    [Fact]
    public void Turning_away_from_a_wall_steps_at_once()
    {
        var (at, dir, blocked) = Wall();
        var s = At(at);
        Walk(s, dir);
        var away = Dirs.First(d => d.Dir != blocked && WorldWalk.Next(Ground, at, d.Dir, ClassDef.DefaultJump, null) is { Slid: false }).Name;

        Walk(s, away);

        Assert.Equal(1, Party(s).Seq);
    }

    [Fact]
    public void A_party_sliding_along_a_wall_keeps_to_the_axis_it_was_on()
    {
        var (tile, dir) = Reached.Order().SelectMany(t => new[] { WalkDir.N, WalkDir.E, WalkDir.S, WalkDir.W }.Select(d => (t, d)))
            .First(x => WorldWalk.Next(Ground, x.t, x.d, ClassDef.DefaultJump, null) is { Slid: true });
        var expected = WorldWalk.Next(Ground, tile, dir, ClassDef.DefaultJump, null)!.Value;
        var s = At(tile);

        Walk(s, dir.ToString().ToLowerInvariant());

        Assert.Equal(expected.To, Party(s).At);
        Assert.Equal(300 * expected.Cost, Party(s).Legs[^1].Ms);
    }

    // ---- What the phones and the TV see -------------------------------------------------------------------------------

    [Fact]
    public void A_step_doesnt_change_any_phone_view()
    {
        var road = Road();
        var s = At(road);
        var before = Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)).ToArray();

        Walk(s, "se");
        Assert.Equal(before, Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)));
        for (var i = 0; i < 25; i++)
        {
            s.Harness.Advance(Ms100);
            Walk(s, "se");
            Assert.Equal(before, Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)));
        }

        Assert.True(Party(s).Seq >= 8);
        for (var i = 0; i < 60 && Party(s).Seq < 20; i++)
        {
            s.Harness.Advance(Ms100);
            Walk(s, "se");
            Assert.Equal(before, Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)));
        }

        Assert.Equal(20, Party(s).Seq);
        Walk(s, null);
        Assert.Equal(before, Enumerable.Range(0, 4).Select(p => PhoneJson(s, p)));
    }

    [Fact]
    public void A_ping_doesnt_change_the_host_view()
    {
        var s = At(Road());
        Walk(s, "se");
        var view = HostJson(s);

        s.Harness.Advance(Ms100);
        Walk(s, "se");

        Assert.Equal(view, HostJson(s));
    }

    [Fact]
    public void Legs_keep_the_last_12()
    {
        var s = At(Road());

        Hold(s, "se", 15);

        Assert.Equal(15, Party(s).Seq);
        Assert.Equal(Enumerable.Range(4, 12), Party(s).Legs.Select(l => l.Seq));
        Assert.Equal(Party(s).At, Party(s).Legs[^1].To);
    }

    [Fact]
    public void The_explore_view_stays_about_a_kilobyte()
    {
        var s = At(Road());

        Hold(s, "se", 15);

        Assert.True(JsonSerializer.SerializeToUtf8Bytes(Party(s), ArcJson.Options).Length < 1024);
    }

    [Fact]
    public void A_notice_goes_once_the_party_moves_off()
    {
        var doc = DocumentAt(Saved(new Tile(-1, 0)));
        var s = new Session(document: doc);
        s.Harness.StartCampaign(s.Lease);
        Assert.NotNull(s.Host.Notice);

        Walk(s, Dirs.First(d => WorldWalk.Next(Ground, Region.Def.Spawns.Start, d.Dir, ClassDef.DefaultJump, null) is not null).Name);

        Assert.Null(s.Host.Notice);
    }

    [Fact]
    public void A_server_restart_is_a_new_epoch_so_a_tv_that_stayed_up_snaps()
    {
        var s = At(Road());
        Hold(s, "se", 3);
        Walk(s, null);
        var epoch = Party(s).Epoch;
        s.Harness.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(epoch, Party(s).Epoch);      // walking never changes it

        var restarted = new Session(document: s.Lease.Document);
        restarted.Harness.Advance(TimeSpan.FromSeconds(30));
        restarted.Harness.Resume(restarted.Lease);

        Assert.NotEqual(epoch, Party(restarted).Epoch);
        Assert.Equal(0, Party(restarted).Seq);
    }

    // ---- Phases -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Leaving_for_the_battle_list_stops_the_party_and_keeps_its_last_step()
    {
        var s = At(Road());
        Hold(s, "se", 2);
        var at = Party(s).At;

        s.Send(0, "battles");
        s.Harness.Advance(TimeSpan.FromSeconds(30));

        Assert.False(Party(s).Held);
        Assert.Equal(at, Party(s).At);
        Assert.Equal(at, TacticsDocument.Read(s.Lease.Document).World!.At);
        Assert.Empty(s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_vip_who_doesnt_lead_can_take_the_party_off_the_road_too()
    {
        var doc = DocumentAt(Saved(Road(), lastLeader: "pr_member1"));
        var s = new Session(document: doc);
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        Walk(s, "se", player: 1);

        s.Send(0, "battles");

        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Equal(Party(s).At, TacticsDocument.Read(s.Lease.Document).World!.At);
        Assert.Empty(s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_leader_who_is_the_vip_can_end_the_session_mid_step()
    {
        var s = At(Road());
        Hold(s, "se", 2);
        var at = Party(s).At;

        s.Send(0, "endSession");

        Assert.True(s.Harness.IsOver);
        Assert.Equal(at, TacticsDocument.Read(s.Lease.Document).World!.At);
        Assert.Empty(s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_battle_comes_back_to_a_party_stood_still_with_the_clock_running()
    {
        var s = At(Road());
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.DoesNotContain(Lead, s.Harness.ActiveTimers);
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);
        s.PlayOut();
        s.Harness.SetConnected(0, true);
        s.Harness.SetConnected(1, true);
        Assert.DoesNotContain(Lead, s.Harness.ActiveTimers);

        s.Send(0, "next");

        Assert.False(Party(s).Held);
        Assert.Equal([Lead], s.Harness.ActiveTimers);
    }

    // ---- The lead -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_leader_leaving_stops_the_party_and_hands_on()
    {
        var s = At(Road());
        Hold(s, "se", 2);
        var at = Party(s).At;

        s.Harness.SetConnected(0, false);

        Assert.False(Party(s).Held);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        Assert.Equal("Player2", Party(s).Leader);
        Assert.Equal(at, TacticsDocument.Read(s.Lease.Document).World!.At);
        Assert.True(s.Screen(1)!.Data.GetProperty("leader").GetBoolean());
    }

    [Fact]
    public void A_press_from_the_old_leader_after_a_handover_is_dropped()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        Walk(s, "se", player: 1);
        var seq = Party(s).Seq;

        Walk(s, "ne");

        Assert.Equal(seq, Party(s).Seq);
        Assert.True(Party(s).Held);        // the new leader is still walking
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);   // and being heard from mid-walk doesn't take it back
    }

    [Fact]
    public void Silence_hands_the_lead_on()
    {
        var s = At(Road());

        s.Harness.Advance(TimeSpan.FromMilliseconds(7999));
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
        s.Harness.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        Assert.Contains(Lead, s.Harness.ActiveTimers);       // and the new leader gets a full 8 s
        s.Harness.Advance(TimeSpan.FromMilliseconds(7999));
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void Idle_pings_keep_the_lead_and_anyone_elses_dont()
    {
        var s = At(Road());

        for (var i = 0; i < 10; i++)
        {
            s.Harness.Advance(TimeSpan.FromSeconds(3));
            Walk(s, null);
        }
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);

        s.Harness.Advance(TimeSpan.FromSeconds(4));
        Walk(s, null, player: 2);
        s.Harness.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_silence_clock_is_one_timer_not_one_for_every_ping()
    {
        var c = new Clock(At(Road()));
        var first = c.Deadline(Lead);

        c.Pass(1000);
        c.Walk(null);

        Assert.Equal(first, c.Deadline(Lead));
        c.Pass(7000);
        c.Fire(Lead);
        Assert.Equal(c.Started + TimeSpan.FromSeconds(9), c.Deadline(Lead));   // 8 s from the last ping
        Assert.Equal(c.Session1.Harness.Players[0].Id, c.Game.Leader!.Id);
    }

    [Fact]
    public void The_lead_clock_stops_when_the_pad_leaves_the_screen()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(7));

        s.Send(0, "battles");
        Assert.DoesNotContain(Lead, s.Harness.ActiveTimers);
        s.Harness.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);

        s.Send(0, "backToWalk");
        s.Harness.Advance(TimeSpan.FromSeconds(7));    // the clock starts again from the pad coming back
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_last_one_here_keeps_the_lead_through_silence()
    {
        var s = At(Road());
        for (var p = 1; p < 4; p++)
            s.Harness.SetConnected(p, false);

        s.Harness.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_silent_session_leader_doesnt_get_the_lead_straight_back()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);

        Walk(s, "se", player: 1);
        Walk(s, null, player: 1);       // a stop is when the session leader would take it back

        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_silent_member_is_better_than_nobody()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(8));        // player 1 takes over from a silent player 0
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        s.Harness.SetConnected(2, false);
        s.Harness.SetConnected(3, false);

        s.Harness.SetConnected(1, false);

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_silent_session_leader_who_speaks_comes_back()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);

        s.Send(0, "endTurn");       // anything at all from that phone

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_silent_session_leader_who_reconnects_comes_back()
    {
        var s = At(Road());
        s.Harness.Advance(TimeSpan.FromSeconds(8));

        s.Harness.SetConnected(0, false);
        s.Harness.SetConnected(0, true);

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_session_leader_takes_it_back_once_stopped()
    {
        var s = At(Road());
        s.Harness.SetConnected(0, false);
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);
        Walk(s, "se", player: 1);

        s.Harness.SetConnected(0, true);
        Assert.Equal(s.Harness.Players[1].Id, s.Game.Leader!.Id);   // not while the party's moving
        Assert.True(Party(s).Held);

        Walk(s, null, player: 1);

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_session_leader_coming_back_to_a_stopped_party_takes_it_at_once()
    {
        var s = At(Road());
        s.Harness.SetConnected(0, false);

        s.Harness.SetConnected(0, true);

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void The_lease_running_out_counts_as_stopped_for_the_session_leader()
    {
        var s = At(Road());
        s.Harness.SetConnected(0, false);
        Walk(s, "se", player: 1);
        s.Harness.SetConnected(0, true);

        s.Harness.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
    }

    [Fact]
    public void A_member_joining_or_leaving_mid_walk_doesnt_stop_the_party()
    {
        var s = At(Road());
        Hold(s, "se", 2);

        s.Harness.SetConnected(3, false);
        Assert.True(Party(s).Held);
        s.Harness.Advance(Ms100);
        Walk(s, "se");
        s.Harness.SetConnected(3, true);

        Assert.True(Party(s).Held);
        Assert.Contains(Step, s.Harness.ActiveTimers);
        var seq = Party(s).Seq;
        s.Harness.Advance(Ms300);
        Assert.True(Party(s).Seq > seq);
    }

    [Fact]
    public void A_character_made_mid_walk_doesnt_stop_the_party()
    {
        var doc = TacticsDocument.Read(DocumentAt(Saved(Road())));
        var s = new Session(document: (doc with { Members = doc.Members.Remove("pr_member4") }).ToJson());
        s.Harness.Resume(s.Lease);
        s.Harness.SetConnected(0, true);
        Walk(s, "se");

        s.Harness.SetConnected(3, true);
        s.Send(3, "pickClass", new { @class = "rogue" });

        Assert.True(Party(s).Held);
        Assert.Equal(1, Party(s).Seq);
        Assert.Contains(Step, s.Harness.ActiveTimers);
        Assert.Equal(4, Party(s).Party.Length);
    }

    [Fact]
    public void Nobody_here_means_no_leader_and_no_timers()
    {
        var s = At(Road());
        Walk(s, "se");

        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);

        Assert.Null(s.Game.Leader);
        Assert.Null(Party(s).Leader);
        Assert.False(Party(s).Held);
        Assert.Empty(s.Harness.ActiveTimers);

        s.Harness.SetConnected(2, true);
        Assert.Equal(s.Harness.Players[2].Id, s.Game.Leader!.Id);
        Assert.Equal([Lead], s.Harness.ActiveTimers);
    }

    [Fact]
    public void After_a_restart_the_first_back_leads_until_the_session_leader_returns()
    {
        var s = At(Road());
        var resumed = new Session(document: s.Lease.Document);
        resumed.Harness.Resume(resumed.Lease);

        resumed.Harness.SetConnected(2, true);
        Assert.Equal(resumed.Harness.Players[2].Id, resumed.Game.Leader!.Id);

        resumed.Harness.SetConnected(0, true);
        Assert.Equal(resumed.Harness.Players[0].Id, resumed.Game.Leader!.Id);
    }

    [Fact]
    public void A_leader_lost_during_a_battle_is_replaced_before_the_pad_comes_back()
    {
        var s = At(Road());
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });

        s.Harness.SetConnected(0, false);
        Assert.Equal("Player2", Party(s).Leader);
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Stopping_saves_within_a_second()
    {
        var s = At(Road());
        Hold(s, "se", 3);
        Walk(s, null);
        var at = Party(s).At;
        Assert.NotEqual(at, TacticsDocument.Read(s.Lease.Document).World!.At);

        s.Harness.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(at, TacticsDocument.Read(s.Lease.Document).World!.At);
        Assert.DoesNotContain(Save, s.Harness.ActiveTimers);
    }

    [Fact]
    public void Stopping_without_moving_saves_nothing()
    {
        var s = At(Road());
        var saves = s.Lease.Saves.Count;

        Walk(s, null);
        s.Harness.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(saves, s.Lease.Saves.Count);
        Assert.DoesNotContain(Save, s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_60_second_walk_saves_at_most_6_times()
    {
        var s = At(Road());
        var saves = s.Lease.Saves.Count;

        for (var i = 0; i < 240; i++)      // up and down the road, pinging as a phone does
        {
            Walk(s, i / 16 % 2 == 0 ? "se" : "nw");
            s.Harness.Advance(TimeSpan.FromMilliseconds(250));
        }
        Walk(s, null);
        s.Harness.Advance(TimeSpan.FromSeconds(2));

        Assert.True(Party(s).Seq > 150, $"only {Party(s).Seq} steps");
        Assert.InRange(s.Lease.Saves.Count - saves, 1, 6);
    }

    [Fact]
    public void Walking_in_fits_and_starts_saves_no_more_often()
    {
        var s = At(Road());
        var saves = s.Lease.Saves.Count;

        for (var i = 0; i < 60; i++)       // half a second's walk, then half a second's rest, for a minute
        {
            Walk(s, i / 12 % 2 == 0 ? "se" : "nw");
            s.Harness.Advance(TimeSpan.FromMilliseconds(500));
            Walk(s, null);
            s.Harness.Advance(TimeSpan.FromMilliseconds(500));
        }
        s.Harness.Advance(TimeSpan.FromSeconds(2));

        Assert.InRange(s.Lease.Saves.Count - saves, 1, 6);
    }

    [Fact]
    public void Any_disconnect_saves_at_once()
    {
        var s = At(Road());
        Hold(s, "se", 2);

        s.Harness.SetConnected(2, false);

        Assert.Equal(Party(s).At, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    [Fact]
    public void A_handover_doesnt_rewrite_who_this_session_belongs_to()
    {
        var s = At(Road());
        s.Harness.SetConnected(0, false);

        Assert.Equal("Player2", Party(s).Leader);
        Assert.Equal("pr_member1", TacticsDocument.Read(s.Lease.Document).World!.LastLeader);
    }

    // ---- All of it ----------------------------------------------------------------------------------------------------

    private static IEnumerable<Tile> Route(Tile from, Tile to)
    {
        var back = new Dictionary<Tile, Tile> { [from] = from };
        var open = new Queue<Tile>([from]);
        while (open.TryDequeue(out var tile) && tile != to)
            foreach (var next in WorldWalk.Steps(Ground, tile, ClassDef.DefaultJump))
                if (back.TryAdd(next, tile))
                    open.Enqueue(next);
        Assert.True(back.ContainsKey(to), $"No way from {from} to {to}.");
        var route = new List<Tile>();
        for (var at = to; at != from; at = back[at])
            route.Add(at);
        route.Reverse();
        return route;
    }

    [Fact]
    public void The_whole_region_can_be_walked()
    {
        var def = Region.Def;
        var s = new Session();
        s.ToWalking();
        var at = def.Spawns.Start;
        var spots = def.Beats.Select(b => b.At).Append(def.Spawns.Recovery).ToArray();
        Assert.True(spots.Length > 1);

        foreach (var spot in spots)
            foreach (var next in Route(at, spot))
            {
                var dir = Dirs.Single(d => WorldWalk.Delta(d.Dir) == (next.X - at.X, next.Y - at.Y)).Name;
                Walk(s, dir);
                Assert.Equal(next, Party(s).At);
                Walk(s, null);
                s.Harness.Advance(TimeSpan.FromMilliseconds(Party(s).Legs[^1].Ms));
                at = next;
            }

        s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(def.Spawns.Recovery, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    /// <summary>
    /// A session whose clock is moved by hand and whose timers only fire when told to, because the harness always fires them on
    /// the dot and a real one is late.
    /// </summary>
    private sealed class Clock : IGameContext
    {
        private readonly Dictionary<string, DateTimeOffset> _timers = [];

        public Clock(Session session)
        {
            // The session's document, on this clock, resumed so that player 0 still leads.
            Session1 = session;
            Game = new TacticsGame(Rules);
            Game.Resume(this, session.Harness.Players, new FakeCampaignLease(session.Lease.Info, session.Lease.Document));
            foreach (var player in session.Harness.Players)
                Game.OnPlayerConnectionChanged(player.Id, true);
            Started = Now;
            Assert.Equal(session.Harness.Players[0].Id, Game.Leader!.Id);
        }

        public Session Session1 { get; }
        public TacticsGame Game { get; }
        public DateTimeOffset Started { get; }
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Random Random { get; } = new(1);
        public int Seq => ((TacticsHostView)Game.HostView()).Explore!.Seq;

        public void Pass(int ms) => Now += TimeSpan.FromMilliseconds(ms);

        public void Walk(string? dir) => Session(0, "walk", new { dir });

        public void Session(int player, string action, object? data = null)
            => Game.OnInput(Session1.Harness.Players[player].Id, new PlayerInput(JsonSerializer.SerializeToElement(new { action, data }, ArcJson.Options)));

        /// <summary>Fires a timer now, late or not, or stale.</summary>
        public void Fire(string key)
        {
            _timers.Remove(key);
            Game.OnTimer(key);
        }

        public DateTimeOffset? Deadline(string key) => TimerDeadline(key);

        public void SetTimer(string key, TimeSpan after) => _timers[key] = Now + after;
        public void CancelTimer(string key) => _timers.Remove(key);
        public DateTimeOffset? TimerDeadline(string key) => _timers.TryGetValue(key, out var at) ? at : null;
        public void EndGame(GameResults results) { }
    }
}
