using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using Session = Tactics.Game.Tests.TacticsGameTests.Session;

namespace Tactics.Game.Tests;

/// <summary>
/// The adapter's watchdog (rules-engine spec §8): an engine failure keeps the state from before, tells the table, never
/// reaches the save, and an AI that hits one skips rather than loops. Failures are forced through the adapter's engine
/// seams, or by a save whose state breaks an invariant, which makes the real <see cref="Battle.Apply"/> throw.
/// </summary>
public sealed class EngineFaultTests
{
    static EngineFaultTests() => Invariants.Enabled = true;

    private static RulesInvariantException Forced() => new("Forced by a test.");

    /// <summary>A game that records the failures it catches.</summary>
    private static (TacticsGame Game, List<EngineFault> Faults) Watched()
    {
        var faults = new List<EngineFault>();
        return (new TacticsGame(TacticsGameTests.Rules) { FaultLogged = faults.Add }, faults);
    }

    private static bool IsHero(UnitId unit) => unit.Value.StartsWith("hero", StringComparison.Ordinal);

    [Fact]
    public void A_players_move_the_engine_fails_on_changes_nothing_and_tells_them_and_the_tv()
    {
        var (game, faults) = Watched();
        var s = new Session(game: game);
        s.ToBattle();
        var (player, screen) = WaitForTurn(s);
        var unit = TacticsGame.UnitFor(player);
        var saved = s.Lease.Document.GetRawText();
        var before = s.Game.Document.Battle!;
        game.ApplyEngine = (state, command, rules, ai) => command is EndTurn ? Battle.Apply(state, command, rules, ai) : throw Forced();

        var path = screen.Data.GetProperty("choices").GetProperty("moves")[0].GetProperty("path");
        s.Send(player, "move", new { path });

        // The room is alive, nothing changed or was saved, and the turn is still theirs.
        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Same(before, s.Game.Document.Battle);
        Assert.Equal(saved, s.Lease.Document.GetRawText());
        Assert.Equal(unit, s.Game.Document.Battle!.State.Active);
        // They're told on their phone, and everyone on the TV.
        var refused = s.Screen(player);
        Assert.Equal(TacticsGame.Screens.Turn, refused?.Screen);
        Assert.Equal(TacticsGame.MoveFailedTryAgain, refused?.Header);
        Assert.Equal(TacticsGame.MoveFailed, s.Host.Notice);

        // What's logged reproduces it: the encounter, seed, beat, command and the state it failed on.
        var fault = Assert.Single(faults);
        Assert.Equal("command", fault.Where);
        Assert.Equal("river_crossing", fault.Encounter);
        Assert.Equal(before.Seed, fault.Seed);
        Assert.Equal(before.Log[^1].Seq, fault.Beat);
        Assert.Equal(before.State.Round, fault.Round);
        Assert.IsType<RulesInvariantException>(fault.Error);
        var command = JsonSerializer.Deserialize<Command>(fault.Command!, ArcJson.Options);
        Assert.Equal(new Move(unit, [.. path.EnumerateArray().Select(t => new Tile(t[0].GetInt32(), t[1].GetInt32()))]).ToString(),
            command?.ToString());
        Assert.Equal(JsonSerializer.Serialize(before.State, ArcJson.Options), fault.State);
        Assert.Contains($"seed {before.Seed}", fault.ToString());

        // Something that works still does, and the failure is gone from the TV once it has been up long enough to read.
        s.Send(player, "endTurn");
        Assert.Contains(s.Game.Document.Battle!.Log[^1].Events, e => e is TurnEnded ended && ended.Unit == unit);
        game.ApplyEngine = Battle.Apply;
        for (var i = 0; i < 20 && s.Host.Notice is not null && s.Game.Phase == TacticsPhase.Battle; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.Null(s.Host.Notice);
    }

    [Fact]
    public void An_ai_step_the_engine_fails_on_ends_that_units_turn_and_the_battle_plays_on()
    {
        var (game, faults) = Watched();
        var s = new Session(game: game);
        s.ToBattle(everyoneAway: true);
        var broken = s.Game.Document.Battle!.State.Order.First(u => !IsHero(u));
        game.ApplyEngine = (state, command, rules, ai)
            => command.Actor == broken && command is not EndTurn ? throw Forced() : Battle.Apply(state, command, rules, ai);

        s.PlayOut();

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        Assert.NotEmpty(faults);
        Assert.All(faults, f => Assert.Equal("ai", f.Where));
        Assert.All(faults, f => Assert.Equal(broken, JsonSerializer.Deserialize<Command>(f.Command!, ArcJson.Options)!.Actor));
        // At most one failure a turn: the turn ends, rather than the AI trying the same thing again.
        Assert.Equal(faults.Count, faults.Select(f => (f.Round, f.TurnIndex)).Distinct().Count());
    }

    [Fact]
    public void A_save_taken_after_an_ai_failure_holds_a_good_state_and_plays_out_when_reopened()
    {
        var (game, faults) = Watched();
        var s = new Session(game: game);
        s.ToBattle(everyoneAway: true);
        var broken = s.Game.Document.Battle!.State.Order.First(u => !IsHero(u));
        game.ApplyEngine = (state, command, rules, ai)
            => command.Actor == broken && command is not EndTurn ? throw Forced() : Battle.Apply(state, command, rules, ai);
        for (var i = 0; i < 200 && faults.Count == 0; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.NotEmpty(faults);

        var saved = TacticsDocument.Read(s.Lease.Document);
        Invariants.Check(saved.Battle!.State, TacticsGameTests.Rules);

        var reopened = new Session(document: s.Lease.Document);
        reopened.Harness.Resume(reopened.Lease);
        reopened.PlayOut();
        Assert.Equal(TacticsPhase.Results, reopened.Game.Phase);
    }

    [Fact]
    public void A_save_whose_next_ai_step_fails_moves_on_the_same_way_each_time_it_is_opened()
    {
        // The seed-108 shape: the save is good, but the AI's next step fails, and it would make the same step on every open.
        var first = new Session();
        first.ToBattle(everyoneAway: true);
        for (var i = 0; i < 6; i++)
            first.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(TacticsPhase.Battle, first.Game.Phase);
        var save = first.Lease.Document;
        var at = TacticsDocument.Read(save).Battle!.State;

        string Open()
        {
            var (game, faults) = Watched();
            game.ApplyEngine = (state, command, rules, ai)
                => state.Round == at.Round && state.TurnIndex == at.TurnIndex && command is not EndTurn
                    ? throw Forced()
                    : Battle.Apply(state, command, rules, ai);
            var s = new Session(document: save, game: game);
            s.Harness.Resume(s.Lease);
            Assert.False(s.Harness.IsOver);
            Assert.Single(faults);
            s.PlayOut();
            Assert.Equal(TacticsPhase.Results, s.Game.Phase);
            return s.Lease.Document.GetRawText();
        }

        Assert.Equal(Open(), Open());
    }

    [Fact]
    public void A_save_the_engine_fails_on_every_command_is_called_off_when_opened_and_opens_cleanly_after()
    {
        // A state that breaks an invariant (a negative resource), so the real engine throws on every command, ending a turn
        // included.
        var first = new Session();
        first.ToBattle(everyoneAway: true);
        for (var i = 0; i < 4; i++)
            first.Harness.Advance(TimeSpan.FromSeconds(7));
        var doc = TacticsDocument.Read(first.Lease.Document);
        var state = doc.Battle!.State;
        var victim = state.Units[state.Order.First(u => u != state.Active)];
        var poisoned = (doc with
        {
            Battle = doc.Battle with
            {
                State = state with { Units = state.Units.SetItem(victim.Id, victim with { Resources = victim.Resources.SetItem("zz_broken", -1) }) },
            },
        }).ToJson();
        var xp = doc.Members.ToDictionary(m => m.Key, m => m.Value.Xp);

        (Session Session, List<EngineFault> Faults) Open(JsonElement document)
        {
            var (game, faults) = Watched();
            var s = new Session(document: document, game: game);
            s.Harness.Resume(s.Lease);
            return (s, faults);
        }

        var (s, faults) = Open(poisoned);
        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Contains(TacticsGame.BattleCalledOff, s.Host.Notice);
        Assert.NotEmpty(faults);
        Assert.All(faults, f => Assert.IsType<RulesInvariantException>(f.Error));
        Assert.Contains(faults, f => f.Command?.Contains("EndTurn", StringComparison.Ordinal) == true);
        // Dropped as if never fought: no battle kept, no XP, no win, no results.
        var after = TacticsDocument.Read(s.Lease.Document);
        Assert.Null(after.Battle);
        Assert.Empty(after.Completed);
        Assert.Equal(xp, after.Members.ToDictionary(m => m.Key, m => m.Value.Xp));
        Assert.Empty(s.Lease.Checkpoints);

        // Opening the poisoned save again goes the same way; opening what it left behind is a plain session.
        Assert.Equal(s.Lease.Document.GetRawText(), Open(poisoned).Session.Lease.Document.GetRawText());
        var (again, none) = Open(s.Lease.Document);
        Assert.Empty(none);
        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
    }

    [Fact]
    public void When_every_unit_fails_every_step_the_battle_is_called_off_after_a_round_of_skipped_turns()
    {
        var (game, faults) = Watched();
        var s = new Session(game: game);
        s.ToBattle(everyoneAway: true);
        var order = s.Game.Document.Battle!.State.Order.Length;
        game.ApplyEngine = (state, command, rules, ai) => command is EndTurn ? Battle.Apply(state, command, rules, ai) : throw Forced();

        s.PlayOut(steps: 500);

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Game.Document.Battle);
        Assert.Contains(TacticsGame.BattleCalledOff, s.Host.Notice);
        Assert.InRange(faults.Count, order + 1, order + 2);
    }

    [Fact]
    public void A_reaction_the_engine_fails_on_is_refused_to_the_player_and_passed_when_it_times_out()
    {
        // An AI reactor is never offered (the engine decides at once), so the adapter answers only a player's prompt: theirs,
        // or the AI rule's when it times out.
        var (game, faults) = Watched();
        var s = new Session(seed: 3, game: game);
        s.ToBattle();
        for (var step = 0; step < 5_000; step++)
        {
            for (var p = 0; p < 4; p++)
            {
                if (s.Screen(p) is { Screen: TacticsGame.Screens.React })
                {
                    var offer = (ReactionOffer)s.Game.Document.Battle!.State.Pending!;
                    var take = offer.Options.First(o => o.Id != Reactions.Pass);
                    game.ApplyEngine = (state, command, rules, ai)
                        => command is React { Choice.Id: not Reactions.Pass } ? throw Forced() : Battle.Apply(state, command, rules, ai);
                    var saved = s.Lease.Document.GetRawText();

                    s.Send(p, "react", new { choice = take.Id });
                    Assert.Same(offer, s.Game.Document.Battle!.State.Pending);
                    Assert.Equal(saved, s.Lease.Document.GetRawText());
                    Assert.Equal(TacticsGame.MoveFailedTryAgain, s.Screen(p)?.Header);
                    Assert.Equal(TacticsGame.MoveFailed, s.Host.Notice);

                    // The prompt times out: the AI rule's answer fails too if it isn't a pass, and the pass goes in its place.
                    s.Harness.Advance(TimeSpan.FromSeconds(11));
                    Assert.NotSame(offer, s.Game.Document.Battle?.State.Pending);
                    Assert.False(s.Harness.IsOver);
                    Assert.InRange(faults.Count, 1, 2);
                    Assert.All(faults, f => Assert.Contains(take.Id, f.Command, StringComparison.Ordinal));

                    game.ApplyEngine = Battle.Apply;
                    s.PlayOut();
                    Assert.Equal(TacticsPhase.Results, s.Game.Phase);
                    return;
                }
                if (s.Screen(p) is { Screen: TacticsGame.Screens.Turn })
                    s.Send(p, "endTurn");
            }
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        }
        Assert.Fail("No reaction was offered to a player.");
    }

    [Fact]
    public void A_battle_the_engine_fails_to_start_leaves_the_vip_on_the_list_with_a_toast()
    {
        var (game, faults) = Watched();
        var s = new Session(game: game);
        game.StartEngine = (_, _, _) => throw Forced();
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 4; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });
        s.Send(0, "battles");

        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Null(s.Game.Document.Battle);
        Assert.Equal(TacticsGame.BattleWontStart, s.Screen(0)?.Header);
        var fault = Assert.Single(faults);
        Assert.Equal(("start", "river_crossing"), (fault.Where, fault.Encounter));

        game.StartEngine = Battle.Start;
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
    }

    [Fact]
    public void A_battle_the_engine_fails_to_restart_under_new_rules_is_called_off()
    {
        var first = new Session();
        first.ToBattle(everyoneAway: true);
        var doc = TacticsDocument.Read(first.Lease.Document);
        var stale = (doc with { Battle = doc.Battle! with { State = doc.Battle.State with { ContentHash = "older-rules" } } }).ToJson();
        var (game, faults) = Watched();
        game.StartEngine = (_, _, _) => throw Forced();
        var s = new Session(document: stale, game: game);

        s.Harness.Resume(s.Lease);

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(TacticsDocument.Read(s.Lease.Document).Battle);
        Assert.Contains(TacticsGame.BattleCalledOff, s.Host.Notice);
        Assert.Equal("start", Assert.Single(faults).Where);
    }

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
}
