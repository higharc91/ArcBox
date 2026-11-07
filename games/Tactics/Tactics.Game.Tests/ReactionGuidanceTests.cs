using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class ReactionGuidanceTests
{
    private static readonly UnitId Wizard = new("hero4");
    private static readonly AbilityId Shield = new("wizard.shield");

    private static Session Prompt(int overAc = 1, bool crit = false)
    {
        var initial = new Session();
        initial.ToBattle();
        var doc = initial.Game.Document;
        var state = doc.Battle!.State;
        var wizard = state.Units[Wizard];
        var foe = state.Units.Values.First(u => u.Sheet.Side == Side.Enemy);
        var roll = new RollResult([crit ? 20 : 10], crit ? 20 : 10, 3, wizard.Sheet.Ac + overAc, wizard.Sheet.Ac, true, crit, false);
        var offer = new ReactionOffer(Wizard, [new(Reactions.Pass, null, null), new(Shield.Value, Shield, Wizard)], "beforeHit",
            new ReactionTrigger(ReactionTriggerKind.BeforeHit, foe.Id, Wizard, RulesPack.CommonAttack.Id, roll));
        state = state with { Units = state.Units.SetItem(Wizard, wizard with { Resources = wizard.Resources.SetItem("mana", 2) }),
            TurnIndex = state.Order.IndexOf(foe.Id), Queue = [], Pending = offer };
        var s = new Session(document: (doc with { Battle = doc.Battle with { State = state } }).ToJson());
        s.Harness.Resume(s.Lease);
        for (var p = 0; p < 4; p++) s.Harness.SetConnected(p, true);
        if (s.Game.Phase == TacticsPhase.Resuming) s.Send(0, "continue");
        Assert.Equal(TacticsGame.Screens.React, s.Screen(3)!.Screen);
        return s;
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, false)]
    [InlineData(1, true, false)]
    public void Shield_preview_respects_the_ac_boundary_and_critical_hits(int overAc, bool crit, bool misses)
    {
        var s = Prompt(overAc, crit);
        var data = s.Screen(3)!.Data;
        var shield = data.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == Shield.Value);
        Assert.Equal(misses, shield.GetProperty("outcome").GetString()!.Contains("into a miss", StringComparison.Ordinal));
        Assert.Contains("1 Reaction", shield.GetProperty("cost").GetString());
        Assert.Contains("1 Mana (2 left)", shield.GetProperty("cost").GetString());
        Assert.True(data.GetProperty("incomingDamage").GetDouble() >= 0);
        var state = s.Game.Document.Battle!.State;
        var offer = Assert.IsType<ReactionOffer>(state.Pending);
        var choice = Ai.React(state, offer, Rules);
        Assert.Contains(ReactionWords.Label(choice, Rules), data.GetProperty("timeout").GetString());
        Assert.Contains(ReactionWords.PassLabel, data.GetProperty("timeout").GetString());
    }

    [Fact]
    public void Recasting_an_existing_shield_does_not_promise_stacking_ac()
    {
        var s = Prompt();
        var state = s.Game.Document.Battle!.State;
        var wizard = state.Units[Wizard];
        var offer = Assert.IsType<ReactionOffer>(state.Pending);
        var shield = new ActiveCondition(new ConditionId("statMod.wizard.shield"), Wizard, 0, false, 0, Stat.Con, false, Mods: new StaticMods(Ac: 5));
        state = state with { Units = state.Units.SetItem(Wizard, wizard with { Conditions = wizard.Conditions.Add(shield) }) };
        offer = offer with { Cause = offer.Cause! with { Roll = offer.Cause.Roll! with { Total = wizard.Sheet.Ac + 6, Vs = wizard.Sheet.Ac + 5 } } };
        var result = Ai.PreviewReaction(state, offer, offer.Options[1], Rules);
        Assert.Equal("This hit still lands.", result.Outcome);
    }

    [Fact]
    public void A_preview_does_not_change_state_or_advance_rng()
    {
        var s = Prompt();
        var state = s.Game.Document.Battle!.State;
        var before = JsonSerializer.Serialize(state, ArcJson.Options);
        var offer = Assert.IsType<ReactionOffer>(state.Pending);
        foreach (var option in offer.Options) Ai.PreviewReaction(state, offer, option, Rules);
        Assert.Equal(before, JsonSerializer.Serialize(s.Game.Document.Battle.State, ArcJson.Options));
    }

    [Fact]
    public void Opportunity_attack_costs_a_reaction_without_the_common_actions_action_cost()
    {
        var s = Prompt();
        var unit = s.Game.Document.Battle!.State.Units[Wizard];
        Assert.Equal("Costs 1 Reaction.", ReactionWords.Cost(new(Reactions.Opportunity, RulesPack.CommonAttack.Id, new UnitId("foe")), unit, Rules));
        Assert.Equal("Costs nothing.", ReactionWords.Cost(new(Reactions.Pass, null, null), unit, Rules));
    }

    [Fact]
    public void Halving_is_explained_even_when_it_does_not_turn_the_hit_into_a_miss()
    {
        var s = Prompt(1, crit: true);
        var state = s.Game.Document.Battle!.State;
        var offer = Assert.IsType<ReactionOffer>(state.Pending);
        var choice = new ReactionChoice("rogue.uncanny_dodge", new AbilityId("rogue.uncanny_dodge"), Wizard);
        Assert.Contains("halves its damage", Ai.PreviewReaction(state, offer, choice, Rules).Outcome);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Counterspell_distinguishes_guaranteed_and_checked_outcomes(int tier)
    {
        var s = Prompt();
        var state = s.Game.Document.Battle!.State;
        var offer = Assert.IsType<ReactionOffer>(state.Pending);
        var actor = offer.Cause!.Actor;
        var spell = Rules.Abilities.Values.First(a => a.Tier == tier);
        state = state with { Queue = [new RunEffectsStep(actor, Wizard, state.Units[Wizard].Pos, spell.Id, spell.Effects)] };
        offer = offer with { Cause = offer.Cause with { Kind = ReactionTriggerKind.SpellCast, Ability = spell.Id, Roll = null }, Trigger = "spellCast" };
        var result = Ai.PreviewReaction(state, offer, new("wizard.counterspell", new AbilityId("wizard.counterspell"), actor), Rules);
        Assert.Contains(spell.Name, result.Outcome);
        Assert.Equal(tier <= 3, result.Outcome.StartsWith("Stops", StringComparison.Ordinal));
        if (tier > 3) Assert.Contains("% chance", result.Outcome);
    }
}
