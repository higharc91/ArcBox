using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class ControlNoticeTests
{
    private static (int Player, CustomView Screen) WaitForTurn(Session s)
    {
        for (var step = 0; step < 2_000; step++)
        {
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn } screen)
                    return (p, screen);
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        }
        throw new Xunit.Sdk.XunitException("No player's turn came up.");
    }

    private static JsonElement Notice(Session s, int player) => s.Screen(player)!.Data.GetProperty("controlNotice");

    [Fact]
    public void Turn_timeout_explains_the_takeover_even_though_the_player_is_connected()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        s.Harness.Advance(TimeSpan.FromSeconds(61));
        var notice = Notice(s, player);
        Assert.Equal("turnTimeout", notice.GetProperty("reason").GetString());
        Assert.Contains("Time ran out", notice.GetProperty("text").GetString());
        Assert.Contains("don't use items", notice.GetProperty("policy").GetString());
        Assert.Contains("timed out", s.Host.Notice);
        Assert.Equal(TacticsGame.Screens.Status, s.Screen(player)!.Screen);
        Assert.True(s.Screen(player)!.Data.GetProperty("aiPlaying").GetBoolean());
        Assert.False(s.Screen((player + 1) % 4)!.Data.TryGetProperty("controlNotice", out _));
    }

    [Fact]
    public void Timeout_notice_survives_the_next_status_then_expires_after_the_following_round()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        var round = s.Game.Document.Battle!.State.Round;
        s.Harness.Advance(TimeSpan.FromSeconds(61));
        var sawLaterStatus = false;
        for (var step = 0; step < 2_000 && s.Game.Document.Battle is { } battle; step++)
        {
            if (s.Screen(player) is { Screen: TacticsGame.Screens.Status } status && battle.State.Active != TacticsGame.UnitFor(player))
            {
                if (battle.State.Round <= round + 1)
                {
                    Assert.Equal("turnTimeout", Notice(s, player).GetProperty("reason").GetString());
                    Assert.Contains("finished your turn", Notice(s, player).GetProperty("text").GetString());
                    sawLaterStatus = true;
                }
                else
                {
                    Assert.True(sawLaterStatus);
                    Assert.False(status.Data.TryGetProperty("controlNotice", out _));
                    return;
                }
            }
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { } screen)
                {
                    if (screen.Screen == TacticsGame.Screens.Turn) s.Send(p, "endTurn");
                    if (screen.Screen == TacticsGame.Screens.React) s.Send(p, "react", new { choice = "pass" });
                }
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Fail("Didn't reach the round after the notice expired.");
    }

    [Fact]
    public void Disconnect_and_return_explain_control_on_both_screens_without_inventing_actions()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        s.Harness.SetConnected(player, false);
        Assert.Equal("disconnected", Notice(s, player).GetProperty("reason").GetString());
        Assert.Contains("one healing item", Notice(s, player).GetProperty("policy").GetString());
        Assert.Contains("Rejoin", s.Host.Notice);
        s.Harness.SetConnected(player, true);
        Assert.Equal(TacticsGame.Screens.Turn, s.Screen(player)!.Screen);
        Assert.Equal("reconnected", Notice(s, player).GetProperty("reason").GetString());
        Assert.Contains("Welcome back", Notice(s, player).GetProperty("text").GetString());
        // Disconnect can immediately execute a command; the recap describes that fact either way.
        Assert.NotEmpty(Notice(s, player).GetProperty("recap").GetString()!);
        Assert.Contains("back in control", s.Host.Notice);
        var seq = Notice(s, player).GetProperty("seq").GetInt32();
        s.Harness.SetConnected(player, true);
        Assert.Equal(seq, Notice(s, player).GetProperty("seq").GetInt32());
    }

    [Fact]
    public void Returning_recap_names_only_successfully_completed_ai_abilities()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        var uses = new List<string>();
        s.Game.ApplyEngine = (state, command, rules, ai) =>
        {
            var result = Battle.Apply(state, command, rules, ai);
            if (result.Rejected is null && command is Use use && use.Actor == TacticsGame.UnitFor(player))
                uses.Add(rules.Abilities[use.Ability].Name);
            return result;
        };
        s.Harness.SetConnected(player, false);
        for (var step = 0; step < 100 && uses.Count == 0; step++)
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.NotEmpty(uses);
        s.Harness.SetConnected(player, true);
        Assert.Contains(uses[^1], Notice(s, player).GetProperty("recap").GetString());
    }

    [Fact]
    public void Failed_handover_does_not_claim_the_player_is_back_in_control()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        s.Harness.SetConnected(player, false);
        s.Game.HandEngine = (_, _, _) => throw new RulesInvariantException("Forced handover failure");
        s.Harness.SetConnected(player, true);
        Assert.Equal("controlUnavailable", Notice(s, player).GetProperty("reason").GetString());
        Assert.DoesNotContain("back in control", Notice(s, player).GetProperty("text").GetString());
        Assert.DoesNotContain("back in control", s.Host.Notice);
    }

    [Fact]
    public void A_timeout_that_ends_the_battle_keeps_its_explanation_on_results()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        s.Game.ApplyEngine = (state, command, rules, ai) =>
        {
            var result = Battle.Apply(state, command, rules, ai);
            return ai && result.Rejected is null ? result with { State = result.State with { Outcome = BattleOutcome.Won } } : result;
        };
        s.Harness.Advance(TimeSpan.FromSeconds(61));
        for (var step = 0; step < 20 && s.Game.Phase == TacticsPhase.Battle; step++)
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        Assert.Equal("turnTimeout", Notice(s, player).GetProperty("reason").GetString());
        Assert.Contains("finished your turn", Notice(s, player).GetProperty("text").GetString());
    }

    [Fact]
    public void Returning_after_the_battle_ends_replaces_the_away_message_and_keeps_the_recap()
    {
        var s = new Session();
        s.ToBattle();
        var (player, _) = WaitForTurn(s);
        s.Game.ApplyEngine = (state, command, rules, ai) =>
        {
            var result = Battle.Apply(state, command, rules, ai);
            return ai && result.Rejected is null ? result with { State = result.State with { Outcome = BattleOutcome.Won } } : result;
        };
        s.Harness.SetConnected(player, false);
        for (var step = 0; step < 20 && s.Game.Phase == TacticsPhase.Battle; step++)
            s.Harness.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        s.Harness.SetConnected(player, true);
        Assert.Equal("reconnected", Notice(s, player).GetProperty("reason").GetString());
        Assert.Contains("battle is over", Notice(s, player).GetProperty("text").GetString());
        Assert.NotEmpty(Notice(s, player).GetProperty("recap").GetString()!);
        Assert.Contains("back in control", s.Host.Notice);
    }

    private static Session ReactionSession()
    {
        var s = new Session();
        s.ToBattle();
        var doc = s.Game.Document;
        var state = doc.Battle!.State;
        var hero = state.Units[TacticsGame.UnitFor(0)];
        var foe = state.Units.Values.First(u => !u.Id.Value.StartsWith("hero", StringComparison.Ordinal));
        var occupied = state.Units.Values.Select(u => u.Pos).ToHashSet();
        var at = new[] { new Tile(foe.Pos.X + 1, foe.Pos.Y), new Tile(foe.Pos.X - 1, foe.Pos.Y), new Tile(foe.Pos.X, foe.Pos.Y + 1), new Tile(foe.Pos.X, foe.Pos.Y - 1) }
            .First(t => t.X >= 0 && t.Y >= 0 && !occupied.Contains(t));
        var offer = new ReactionOffer(hero.Id,
            [new ReactionChoice(Reactions.Pass, null, null), new ReactionChoice(Reactions.Opportunity, RulesPack.CommonAttack.Id, foe.Id)],
            "leavesReach", new ReactionTrigger(ReactionTriggerKind.LeavesReach, foe.Id, null, null, null, new Tile(foe.Pos.X + 5, foe.Pos.Y)));
        state = state with { Units = state.Units.SetItem(hero.Id, hero with { Pos = at, Economy = hero.Economy with { Reaction = true } }),
            TurnIndex = state.Order.IndexOf(foe.Id), Queue = [], Pending = offer };
        var resumed = new Session(document: (doc with { Battle = doc.Battle with { State = state } }).ToJson());
        resumed.Harness.Resume(resumed.Lease);
        for (var p = 0; p < 4; p++) resumed.Harness.SetConnected(p, true);
        if (resumed.Game.Phase == TacticsPhase.Resuming) resumed.Send(0, "continue");
        Assert.Equal(TacticsGame.Screens.React, resumed.Screen(0)!.Screen);
        return resumed;
    }

    [Theory]
    [InlineData("ability")]
    [InlineData("pass")]
    [InlineData("invalid")]
    [InlineData("fault")]
    public void Reaction_timeout_reports_the_actual_answer_including_fallback_pass(string choice)
    {
        var s = ReactionSession();
        s.Game.ReactEngine = (_, offer, _) => choice switch
        {
            "ability" => offer.Options[1],
            "pass" => offer.Options[0],
            "invalid" => new ReactionChoice("not-offered", null, null),
            _ => throw new RulesInvariantException("Forced reaction failure"),
        };
        s.Harness.Advance(TimeSpan.FromSeconds(11));
        var expected = choice == "ability" ? Rules.Abilities[RulesPack.CommonAttack.Id].Name : "Don't react";
        Assert.Equal("reactionTimeout", Notice(s, 0).GetProperty("reason").GetString());
        Assert.Contains(expected, Notice(s, 0).GetProperty("text").GetString());
        Assert.Contains(expected, s.Host.Notice);
    }

    [Fact]
    public void A_manual_reaction_is_not_labelled_as_a_timeout()
    {
        var s = ReactionSession();
        s.Send(0, "react", new { choice = Reactions.Pass });
        Assert.False(s.Screen(0)!.Data.TryGetProperty("controlNotice", out _));
    }
}
