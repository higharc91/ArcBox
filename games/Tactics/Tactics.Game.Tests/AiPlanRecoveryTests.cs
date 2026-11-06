using System.Collections.Immutable;
using Tactics.Logic;
using Session = Tactics.Game.Tests.TacticsGameTests.Session;

namespace Tactics.Game.Tests;

public sealed class AiPlanRecoveryTests
{
    private static readonly UnitId Actor = new("hero1");
    private static readonly AbilityId Hex = new("warlock.hex");
    private static readonly AbilityId Blast = new("warlock.eldritch_blast");
    private static readonly AbilityId Rebuke = new("warlock.hellish_rebuke");

    private sealed class Trace
    {
        public List<BattleEvent> Events { get; } = [];
        public List<Rejection> Refusals { get; } = [];
        public int Plans { get; set; }
    }

    private static Trace Play(bool permanentlyStale)
    {
        var original = new Session();
        original.ToBattle("warlord_bridge");
        var doc = original.Game.Document;
        var state = doc.Battle!.State;
        var rules = TacticsGameTests.Rules;
        var foe = state.Units.Values.First(u => u.Sheet.Side == Side.Enemy);
        var character = SheetBuilder.DefaultCharacter("Wex", "human", "warlock", null, 1, rules);
        var sheet = SheetBuilder.ForCharacter(character, rules) with { MaxHp = 100, Ac = 0, Abilities = [Hex, Blast, Rebuke] };
        var hero = state.Units[Actor] with { Sheet = sheet, Hp = 100, Pos = new(1, 4), TempHp = 0,
            Resources = Ordinal.Map(("pact", permanentlyStale ? 0 : 1)), Economy = Economy.Fresh(sheet), Conditions = [] };
        foe = foe with { Pos = new(1, 3), Hp = 100, Conditions = [],
            Sheet = foe.Sheet with { MaxHp = 100, Ac = 0, Weapon = foe.Sheet.Weapon with { Damage = Dice.Parse("1") } } };
        state = state with { Units = state.Units.SetItem(Actor, hero).SetItem(foe.Id, foe),
            Order = [Actor, .. state.Order.Where(id => id != Actor)], TurnIndex = 0, Pending = null, Queue = [], Rng = Rng.Seeded(7) };
        var trace = new Trace();
        var game = new TacticsGame(rules, battleList: true);
        game.PlanEngine = (current, unit, pack, options) =>
        {
            if (unit != Actor) return Ai.Plan(current, unit, pack, options);
            trace.Plans++;
            if (trace.Plans == 1 || permanentlyStale)
            {
                ImmutableArray<Command> commands = permanentlyStale
                    ? [new Use(Actor, Hex, new Target(Unit: foe.Id))]
                    : [new Move(Actor, [new(1, 4), new(2, 4), new(3, 4)]),
                       new Use(Actor, Hex, new Target(Unit: foe.Id)), new EndTurn(Actor)];
                return new TurnPlan(commands, 0, "Resource-spending reaction regression");
            }
            Assert.Equal(0, current.Units[Actor].Resources["pact"]);
            Assert.True(current.Units[Actor].Economy.Action);
            return Ai.Plan(current, unit, pack, options);
        };
        game.ApplyEngine = (current, command, pack, finishing) =>
        {
            var result = Battle.Apply(current, command, pack, finishing);
            trace.Events.AddRange(result.Events);
            if (result.Rejected is { } rejection) trace.Refusals.Add(rejection);
            return result;
        };
        doc = doc with { Battle = doc.Battle with { State = state, Log = [] },
            Members = doc.Members.SetItem("pr_member1", doc.Members["pr_member1"] with { Character = character }) };
        var s = new Session(document: doc.ToJson(), game: game);
        s.Harness.Resume(s.Lease);
        for (var p = 0; p < 4; p++) s.Harness.SetConnected(p, true);
        if (s.Game.Phase == TacticsPhase.Resuming) s.Send(0, "continue");
        s.Harness.SetConnected(0, false);
        for (var i = 0; i < 80 && !trace.Events.Any(e => e is TurnEnded t && t.Unit == Actor); i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
        Assert.Contains(trace.Events, e => e is TurnEnded t && t.Unit == Actor);
        return trace;
    }

    [Fact]
    public void Rebuke_spending_the_last_pact_during_a_walk_does_not_forfeit_the_remaining_action()
    {
        var trace = Play(permanentlyStale: false);
        Assert.Contains(trace.Events, e => e is AbilityUsed a && a.Source == Actor && a.Ability == Rebuke);
        Assert.Contains(trace.Refusals, r => r.Code == RejectCode.CantAfford);
        Assert.Equal(2, trace.Plans);
        var rebuke = trace.Events.FindIndex(e => e is AbilityUsed a && a.Source == Actor && a.Ability == Rebuke);
        var blast = trace.Events.FindIndex(e => e is AbilityUsed a && a.Source == Actor && a.Ability == Blast);
        var end = trace.Events.FindIndex(e => e is TurnEnded t && t.Unit == Actor);
        Assert.True(rebuke < blast && blast < end);
    }

    [Fact]
    public void A_repeatedly_refused_fresh_plan_is_bounded_and_eventually_ends_the_turn()
    {
        var trace = Play(permanentlyStale: true);
        Assert.Equal(2, trace.Plans);
        Assert.Equal(2, trace.Refusals.Count(r => r.Code == RejectCode.CantAfford));
    }
}
