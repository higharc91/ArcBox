using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// The AI and the rogue's Hide (#108): Hide is refused in plain view, so the planner never plans it there, and still takes
/// it where the plan ends out of sight (the planner asks the engine's targeting, which checks the requirement).
/// </summary>
public sealed class HideAiTests
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

    /// <summary>A human rogue of <paramref name="level"/> on the AI's skirmisher profile, acting first, and goblins at the given tiles.</summary>
    private static (BattleState State, RulesPack Rules) Start(int level, Tile rogue, params Tile[] goblins)
    {
        var base_ = BasePackTests.Rules;
        var map = AsciiMap.Parse(Field);
        var rules = RulesPack.InMemory("hide-ai", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values, aiProfiles: base_.AiProfiles.Values);
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("vex", "human", "rogue", null, level, base_), base_)
            with { InitiativeBonus = 50 };
        var setup = new BattleSetup(map,
        [
            new UnitPlacement(U("vex"), sheet, new AiControlled("skirmisher"), rogue, []),
            .. goblins.Select((at, i) => new UnitPlacement(U($"gob{i + 1}"), SheetBuilder.ForMonster("goblin", base_) with { MaxHp = 200 },
                new AiControlled("brute"), at, [])),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed: 4).State;
        Assert.Equal(U("vex"), state.Active);
        return (state, rules);
    }

    private static void ApplyAll(BattleState state, TurnPlan plan, RulesPack rules)
    {
        foreach (var command in plan.Commands)
        {
            var resolution = Battle.Apply(state, command, rules);
            Assert.True(resolution.Rejected is null, $"{command} was refused: {resolution.Rejected?.Reason}\n{plan.Explain}");
            state = resolution.State;
        }
    }

    [Fact]
    public void A_rogue_fighting_in_plain_view_plans_no_hide()
    {
        var (state, rules) = Start(3, T(1, 3), T(2, 3));

        var plan = Ai.Plan(state, U("vex"), rules);

        Assert.Contains(plan.Commands, c => c is Use { Ability.Value: "common.attack" });
        Assert.DoesNotContain(plan.Commands, c => c is Use { Ability: var a } && a == Hide);
        ApplyAll(state, plan, rules);
    }

    [Fact]
    public void A_rogue_whose_turn_is_spent_out_of_sight_hides()
    {
        // Its action and Move are spent behind the wall, out of the goblin's sight: the bonus action is left, and Hide is legal.
        var (state, rules) = Start(1, T(2, 2), T(7, 2));
        state = state.Change("vex", u => u with { Economy = u.Economy with { Action = false, MoveLeft = 0 } });

        var plan = Ai.Plan(state, U("vex"), rules);

        Assert.True(plan.Commands is [Use { Ability.Value: "rogue.cunning_hide" }, EndTurn], plan.Explain);
        ApplyAll(state, plan, rules);
    }
}
