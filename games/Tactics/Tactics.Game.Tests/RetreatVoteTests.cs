using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>A party votes to fall back from a losing battle: it ends as a flight (#111).</summary>
public sealed class RetreatVoteTests
{
    /// <summary>A battle at <paramref name="round"/>, with the players in <paramref name="here"/> connected (all four by default).</summary>
    internal static Session Fight(int round = 3, string encounter = "river_crossing", Func<BattleState, BattleState>? edit = null, params int[] here)
    {
        var s = new Session();
        s.ToBattle(encounter);
        var doc = TacticsDocument.Read(s.Lease.Document);
        var state = doc.Battle!.State with { Round = round };
        state = edit?.Invoke(state) ?? state;
        var resumed = new Session(document: (doc with { Battle = doc.Battle with { State = state } }).ToJson());
        resumed.Harness.Resume(resumed.Lease);
        foreach (var p in here.Length == 0 ? [0, 1, 2, 3] : here)
            resumed.Harness.SetConnected(p, true);
        if (resumed.Game.Phase == TacticsPhase.Resuming)
            resumed.Send(here.Length == 0 ? 0 : here[0], "continue");
        Assert.Equal(TacticsPhase.Battle, resumed.Game.Phase);
        return resumed;
    }

    private static RetreatView? Vote(Session s) => s.Host.Battle!.Retreat;

    private static string? Panel(Session s, int player, string field)
        => s.Screen(player)!.Data.TryGetProperty("fallBack", out var f) && f.TryGetProperty(field, out var v) ? v.ToString() : null;

    private static string? Open(Session s, int player, string field)
        => s.Screen(player)!.Data.TryGetProperty("fallBack", out var f) && f.TryGetProperty("vote", out var v) && v.TryGetProperty(field, out var x)
            && x.ValueKind != System.Text.Json.JsonValueKind.Null ? x.ToString() : null;

    private static void Yes(Session s, int player) => s.Send(player, "fallBackVote", new { yes = true });

    private static void No(Session s, int player) => s.Send(player, "fallBackVote", new { yes = false });

    [Fact]
    public void Proposing_is_refused_before_round_three()
    {
        var s = Fight(round: 2);
        Assert.Null(Panel(s, 1, "canPropose"));

        s.Send(1, "fallBack");

        Assert.Null(Vote(s));
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.False(string.IsNullOrEmpty(s.Screen(1)!.Header));
    }

    [Fact]
    public void Any_present_player_can_propose_from_round_three_on_anyones_turn()
    {
        var s = Fight(round: 3);
        var active = s.Game.Document.Battle!.State.Active;
        var other = Enumerable.Range(0, 4).First(p => TacticsGame.UnitFor(p) != active);
        Assert.Equal("True", Panel(s, other, "canPropose"));

        s.Send(other, "fallBack");

        var vote = Vote(s)!;
        Assert.Equal(3, vote.Needed);
        Assert.Equal(4, vote.Voters.Length);
        Assert.Equal("yes", vote.Voters.Single(v => v.Name == $"Player{other + 1}").Vote);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Contains("retreat", s.Harness.ActiveTimers);
        for (var p = 0; p < 4; p++)
        {
            Assert.Equal("3", Open(s, p, "needed"));
            Assert.Equal(p == other ? "yes" : null, Open(s, p, "mine"));
        }
    }

    [Fact]
    public void A_strict_majority_of_the_present_players_ends_the_battle_Fled()
    {
        var s = Fight(round: 3);
        s.Send(0, "fallBack");
        No(s, 1);
        Yes(s, 2);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);     // 2 of 4 isn't a strict majority

        Yes(s, 3);

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        Assert.Equal("Fled", s.Host.Outcome);
        Assert.DoesNotContain("retreat", s.Harness.ActiveTimers);
    }

    [Fact]
    public void Absent_players_dont_count_toward_the_majority()
    {
        var s = Fight(round: 3, here: [0, 1]);
        s.Send(0, "fallBack");
        Assert.Equal(2, Vote(s)!.Needed);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);

        Yes(s, 1);

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
    }

    [Fact]
    public void A_lone_present_player_carries_it_alone()
    {
        var s = Fight(round: 3, here: [2]);

        s.Send(2, "fallBack");

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
    }

    [Fact]
    public void A_vote_that_cant_carry_fails_and_cant_be_proposed_again_until_the_next_round()
    {
        var s = Fight(round: 3);
        s.Send(0, "fallBack");
        No(s, 1);
        No(s, 2);

        Assert.Null(Vote(s));
        Assert.DoesNotContain("retreat", s.Harness.ActiveTimers);
        Assert.Equal("True", Panel(s, 1, "failed"));
        Assert.Null(Panel(s, 1, "canPropose"));
        s.Send(1, "fallBack");
        Assert.Null(Vote(s));

        for (var i = 0; i < 40 && s.Game.Document.Battle!.State.Round == 3 && s.Game.Phase == TacticsPhase.Battle; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal("True", Panel(s, 1, "canPropose"));
        s.Send(1, "fallBack");
        Assert.NotNull(Vote(s));
    }

    [Fact]
    public void An_unanswered_vote_times_out_after_20_seconds_and_fails()
    {
        var s = Fight(round: 3);
        s.Send(0, "fallBack");
        Yes(s, 1);

        s.Harness.Advance(TimeSpan.FromSeconds(19));
        Assert.NotNull(Vote(s));
        s.Harness.Advance(TimeSpan.FromSeconds(2));

        Assert.Null(Vote(s));
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal("True", Panel(s, 2, "failed"));
    }

    [Fact]
    public void A_player_who_leaves_mid_vote_stops_counting()
    {
        var s = Fight(round: 3);
        s.Send(0, "fallBack");
        Yes(s, 1);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);

        s.Harness.SetConnected(3, false);     // 2 of 3 present is a strict majority

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
    }

    [Fact]
    public void An_encounter_that_forbids_retreat_offers_none()
    {
        var s = Fight(round: 5, edit: st => st with { NoRetreat = true });

        Assert.Null(Panel(s, 0, "canPropose"));
        s.Send(0, "fallBack");
        Assert.Null(Vote(s));
    }

    [Fact]
    public void A_flight_pays_half_xp_keeps_injuries_gives_no_loot_and_doesnt_complete_the_encounter()
    {
        var injury = Rules.Injuries.Keys.First();
        var s = Fight(round: 3, edit: st => st with
        {
            Xp = 251, Loot = [new LootTableId("goblin_cache")], Injuries = [new RecordedInjury(TacticsGame.UnitFor(1), injury)],
        });
        var before = TacticsDocument.Read(s.Lease.Document);

        s.Send(0, "fallBack");
        Yes(s, 1);
        Yes(s, 2);

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        var after = TacticsDocument.Read(s.Lease.Document);
        Assert.Null(after.Battle);
        Assert.DoesNotContain("river_crossing", after.Completed);
        Assert.True(after.World!.RestReady);     // a flight earns a rest, like a loss
        foreach (var (id, member) in after.Members)
            Assert.Equal(before.Members[id].Xp + 125, member.Xp);
        Assert.Contains(injury, after.Members["pr_member2"].Character.Injuries);
        Assert.Equal(before.Gold, after.Gold);
        Assert.Equal(before.Members.Values.Select(m => m.Gold), after.Members.Values.Select(m => m.Gold));
        var (_, label) = Assert.Single(s.Lease.Checkpoints);
        Assert.Equal("After River Crossing", label);
        Assert.Equal("Fled", s.Screen(0)!.Data.GetProperty("outcome").GetString());
    }

    [Fact]
    public void The_VIPs_results_screen_after_a_flight_carries_what_the_difficulty_hint_needs()
    {
        var s = Fight(round: 3);
        s.Send(0, "fallBack");
        Yes(s, 1);
        Yes(s, 2);

        var data = s.Screen(0)!.Data;
        Assert.True(data.GetProperty("vip").GetBoolean());
        Assert.Equal("Normal", data.GetProperty("difficulty").GetString());
        Assert.Equal("Fled", data.GetProperty("outcome").GetString());
    }

    [Fact]
    public void A_player_playing_their_summon_proposes_and_votes_like_anyone()
    {
        var hound = new UnitId("hero1.hound.7");
        var s = Fight(round: 3, edit: state =>
        {
            var owner = state.Units[new UnitId("hero1")];
            var taken = state.Units.Values.Select(u => u.Pos).ToHashSet();
            var at = Enumerable.Range(0, 12).SelectMany(x => Enumerable.Range(0, 12).Select(y => new Tile(x, y))).First(t => !taken.Contains(t));
            var sheet = owner.Sheet with { Name = "Hound" };
            var unit = owner with
            {
                Id = hound, Sheet = sheet, SummonedBy = owner.Id, Pos = at, Economy = Economy.Fresh(sheet),
                Controller = new PlayerControlled(new ProfileId("pr_member1")),
            };
            return state with { Units = state.Units.Add(hound, unit), Order = [hound, .. state.Order], TurnIndex = 0, Pending = null };
        });
        Assert.Equal(hound, s.Game.Document.Battle!.State.Active);
        Assert.Equal(TacticsGame.Screens.Turn, s.Screen(0)!.Screen);
        Assert.Equal("True", Panel(s, 0, "canPropose"));

        s.Send(0, "fallBack");
        Assert.NotNull(Vote(s));
        Yes(s, 1);
        Yes(s, 2);

        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
    }
}
