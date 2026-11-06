using System.Collections.Immutable;
using ArcBox.Sdk;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

/// <summary>
/// The simulator's scripted "sets up Sneak Attack every turn" rogue (#108, <see cref="HiddenRogue"/>): it hides whenever the
/// rules let it and attacks from hiding, with legal commands only.
/// </summary>
public sealed class HiddenRogueTests
{
    /// <summary>A half-cover crate at (3, 0) and a wall block (blocks sight) at (3, 2).</summary>
    private const string Field = """
        0. 0. 0. 0c 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0C 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Hide = new("rogue.cunning_hide");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A human rogue (level 3 unless given: Move 4, Cunning Dash), acting first, hiding if asked, and a sturdy goblin.</summary>
    private static (BattleState State, RulesPack Rules) Start(Tile rogue, Tile goblin, bool hiding = false, int level = 3)
    {
        var base_ = BasePackTests.Rules;
        var map = AsciiMap.Parse(Field);
        var rules = RulesPack.InMemory("scripted", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values);
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("vex", "human", "rogue", null, level, base_), base_)
            with { InitiativeBonus = 50 };
        var setup = new BattleSetup(map,
        [
            new UnitPlacement(U("vex"), sheet, new AiControlled("skirmisher"), rogue, []),
            new UnitPlacement(U("gob"), SheetBuilder.ForMonster("goblin", base_) with { MaxHp = 200 }, new AiControlled("brute"), goblin, []),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed: 5).State;
        Assert.Equal(U("vex"), state.Active);
        if (hiding)
            state = Battle.Apply(state, new Use(U("vex"), Hide, new Target()), rules).State
                .Change("vex", u => u with { Economy = u.Economy with { Bonus = true } });
        return (state, rules);
    }

    /// <summary>A command in words, so plans compare by value (a <see cref="Move"/>'s path is an array).</summary>
    private static string Said(Command command) => command switch
    {
        Move move => "walk " + string.Join(" ", move.Path.Select(t => $"({t.X}, {t.Y})")),
        Use use => $"use {use.Ability} on {use.Target.Unit}",
        EndTurn => "end turn",
        _ => command.ToString(),
    };

    private static bool IsHiding(BattleState state) => state.Units[U("vex")].Conditions.Any(c => c.Id.Value == "hiding");

    [Fact]
    public void Hidden_it_steps_out_strikes_with_advantage_and_hides_again()
    {
        // Out of the goblin's sight round the wall's corner: one step to strike, one step back.
        var (state, rules) = Start(T(3, 1), T(4, 2), hiding: true);

        var plan = HiddenRogue.Plan(state, U("vex"), rules);

        Assert.Equal(["walk (3, 1) (4, 1)", "use common.attack on gob", "walk (4, 1) (3, 1)", "use rogue.cunning_hide on ", "end turn"],
            plan!.Value.Select(Said));
        var events = new List<BattleEvent>();
        foreach (var command in plan.Value)
        {
            var applied = Battle.Apply(state, command, rules);
            Assert.Null(applied.Rejected);
            state = applied.State;
            events.AddRange(applied.Events);
        }
        Assert.Equal(2, events.OfType<Rolled>().First(r => r.Roller == U("vex") && r.Kind == RollKind.Attack).Result.D20s.Length);
        Assert.Contains(new ConditionRemoved(U("vex"), new ConditionId("hiding")), events);   // revealed by the attack…
        Assert.True(IsHiding(state));                                                          // …and hidden again
    }

    [Fact]
    public void Seen_it_strikes_and_hides_rather_than_hide_and_strike()
    {
        // Ending the turn hidden buys the next turn's advantage and the foes' disadvantage meanwhile.
        var (state, rules) = Start(T(3, 1), T(4, 2));

        var plan = HiddenRogue.Plan(state, U("vex"), rules)!.Value;

        Assert.Equal<Command>(
            [new Use(U("vex"), RulesPack.CommonAttack.Id, new Target(Unit: U("gob"))), new Use(U("vex"), Hide, new Target())],
            plan.OfType<Use>());
    }

    [Fact]
    public void With_no_way_back_to_cover_it_hides_first_and_strikes_from_hiding()
    {
        // Every tile beside the goblin is the whole Move away from the wall.
        var (state, rules) = Start(T(2, 2), T(5, 2));

        var plan = HiddenRogue.Plan(state, U("vex"), rules)!.Value;

        Assert.Equal(new Use(U("vex"), Hide, new Target()), plan[0]);
        Assert.Equal(T(4, 2), Assert.IsType<Move>(plan[1]).Path[^1]);
        Assert.Equal(new Use(U("vex"), RulesPack.CommonAttack.Id, new Target(Unit: U("gob"))), plan[2]);
        Assert.Equal(new EndTurn(U("vex")), plan[3]);
    }

    [Fact]
    public void With_no_foe_in_reach_it_hides_where_it_can_strike_from_next_turn()
    {
        // Level 1: no Cunning Dash to close the gap this turn.
        var (state, rules) = Start(T(0, 3), T(6, 3), level: 1);

        var plan = HiddenRogue.Plan(state, U("vex"), rules)!.Value;

        Assert.Equal(3, plan.Length);
        Assert.Equal(T(2, 2), Assert.IsType<Move>(plan[0]).Path[^1]);
        Assert.Equal<Command>([new Use(U("vex"), Hide, new Target()), new EndTurn(U("vex"))], plan[1..]);
    }

    [Fact]
    public void With_a_foe_in_reach_of_a_dash_it_leaves_the_turn_to_the_ai_to_dash_in_and_strike()
    {
        // Level 3: Cunning Dash brings the goblin in reach this turn, so it strikes rather than creep to cover.
        var (state, rules) = Start(T(0, 3), T(6, 3));

        Assert.Null(HiddenRogue.Plan(state, U("vex"), rules));
    }

    [Fact]
    public void A_helped_strike_has_advantage_so_the_rogue_takes_it()
    {
        // As A_plain_strike_with_no_cover_in_reach_is_the_ais_turn, but an ally helped: the strike has advantage after all.
        var (state, rules) = Start(T(6, 0), T(7, 1));
        state = state.Give("vex", new ActiveCondition(RulesPack.Helped.Id, U("vex"), 1, false, 0, Stat.Con, false));

        var plan = HiddenRogue.Plan(state, U("vex"), rules);

        Assert.Contains("use common.attack on gob", plan!.Value.Select(Said));
    }

    [Fact]
    public void Far_from_the_foes_it_leaves_the_turn_to_the_ai_to_close_in()
    {
        // Behind the wall is the only cover, more than a turn's walk from the goblin: the AI dashes in faster.
        Assert.Null(HiddenRogue.Plan(Start(T(0, 3), T(7, 3)).State, U("vex"), Start(T(0, 3), T(7, 3)).Rules));
    }

    [Fact]
    public void A_plain_strike_with_no_cover_in_reach_is_the_ais_turn()
    {
        // No tile in reach to hide on before or after striking, and no flank: nothing to set up.
        var (state, rules) = Start(T(6, 0), T(7, 1));

        Assert.Null(HiddenRogue.Plan(state, U("vex"), rules));
    }

    [Fact]
    public void Hidden_with_no_cover_nearer_the_foe_it_leaves_the_turn_to_the_ai_to_close_in()
    {
        // Out of the goblin's sight only behind the wall, where it stands: hiding again would wait there for ever.
        var (state, rules) = Start(T(2, 2), T(7, 2), hiding: true);

        Assert.Null(HiddenRogue.Plan(state, U("vex"), rules));
    }

    [Fact]
    public void After_a_refused_scripted_command_the_turn_is_the_ais()
    {
        // A script whose command the engine refuses: the turn is planned again by the AI, not by the script, and played out.
        var (state, rules) = Start(T(0, 3), T(7, 3));
        var tally = new Tally(rules);
        var rogues = new RogueTally(rules);
        var ai = new SimRunner.AiCounts();
        ImmutableArray<Command>? Refused(BattleState _) => [new Use(U("vex"), new AbilityId("rogue.no_such_trick"), new Target()), new EndTurn(U("vex"))];

        var after = SimRunner.PlayTurn(state, U("vex"), rules, tally, ai, rogues, Refused);

        Assert.NotEqual(U("vex"), after.Active);
        Assert.Equal((1, 0), (ai.Rejected, ai.ForcedEnds));
        var reason = Assert.Single(ai.RefusalReasons);
        Assert.StartsWith("rogue.no_such_trick / ", reason.Key);
        Assert.Equal(ai.Rejected, reason.Value);
        Assert.Equal(1, rogues.SetUp.Refused);
    }

    [Fact]
    public void A_unit_without_hide_has_no_scripted_turn()
    {
        var (state, rules) = Start(T(0, 3), T(7, 3));
        state = state.Change("vex", u => u with { Sheet = u.Sheet with { Abilities = u.Sheet.Abilities.Remove(Hide) } });

        Assert.Null(HiddenRogue.Plan(state, U("vex"), rules));
    }
}
