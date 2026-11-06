using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// The rogue's Hide with the shipped content (#108): refused next to a foe or in plain view, allowed out of sight or behind
/// cover, and lost when the rogue ends its turn in plain view. Plain view: a foe that can act stands next to the unit, or
/// sees it (line of sight) with no cover between (<see cref="Stealth.InPlainView"/>).
/// </summary>
public sealed class HideTests
{
    /// <summary>A wall block (blocks sight) at (3, 2) and a half-cover crate at (3, 0).</summary>
    private const string Field = """
        0. 0. 0. 0c 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0C 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Hide = new("rogue.cunning_hide");

    private static readonly ConditionId Hiding = new("hiding");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A level-1 human rogue built from the shipped class (acting first), and goblins at the given tiles.</summary>
    private static (BattleState State, RulesPack Rules) Start(Tile rogue, params Tile[] goblins)
    {
        var base_ = BasePackTests.Rules;
        var map = AsciiMap.Parse(Field);
        var rules = RulesPack.InMemory("hide", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values);
        var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("vex", "human", "rogue", null, 1, base_), base_)
            with { InitiativeBonus = 50 };
        var goblin = SheetBuilder.ForMonster("goblin", base_);
        var setup = new BattleSetup(map,
        [
            new UnitPlacement(U("vex"), sheet, new PlayerControlled(new ProfileId("pr_vex")), rogue, []),
            .. goblins.Select((at, i) => new UnitPlacement(U($"gob{i + 1}"), goblin, new AiControlled("brute"), at, [])),
        ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, rules, seed: 3).State;
        Assert.Equal(U("vex"), state.Active);
        return (state, rules);
    }

    private static Resolution HideAt(Tile rogue, params Tile[] goblins)
    {
        var (state, rules) = Start(rogue, goblins);
        return Battle.Apply(state, new Use(U("vex"), Hide, new Target()), rules);
    }

    private static bool IsHiding(BattleState state) => state.Units[U("vex")].Conditions.Any(c => c.Id == Hiding);

    [Fact]
    public void Hide_is_refused_next_to_a_foe()
    {
        var refused = HideAt(T(1, 3), T(2, 3));

        Assert.Equal(RejectCode.Unmet, refused.Rejected?.Code);
        Assert.Equal("Only when you aren't in plain view.", refused.Rejected?.Reason);
    }

    [Fact]
    public void Hide_is_refused_next_to_a_foe_even_with_cover_from_it()
    {
        // The goblin stands on the crate's tile: the crate gives the rogue half cover from it, but it is right there.
        var refused = HideAt(T(2, 0), T(3, 0));

        Assert.Equal(RejectCode.Unmet, refused.Rejected?.Code);
    }

    [Fact]
    public void Hide_is_refused_in_the_open_in_sight_of_a_foe()
    {
        var (state, rules) = Start(T(0, 3), T(6, 3));

        Assert.Equal(RejectCode.Unmet, Battle.Check(state, new Use(U("vex"), Hide, new Target()), rules)?.Code);
        Assert.Equal(RejectCode.Unmet, Battle.Apply(state, new Use(U("vex"), Hide, new Target()), rules).Rejected?.Code);
    }

    [Fact]
    public void Hide_works_behind_a_wall()
    {
        var hidden = HideAt(T(2, 2), T(6, 2));

        Assert.Null(hidden.Rejected);
        Assert.True(IsHiding(hidden.State));
    }

    [Fact]
    public void Hide_works_behind_half_cover()
    {
        // The crate at (3, 0) stands between the rogue and the goblin, next to the rogue: half cover.
        var hidden = HideAt(T(2, 0), T(6, 0));

        Assert.Null(hidden.Rejected);
        Assert.True(IsHiding(hidden.State));
    }

    [Fact]
    public void Hide_needs_cover_from_every_foe_that_sees_the_rogue()
    {
        // Behind the wall from one goblin, in the open for the other.
        Assert.Equal(RejectCode.Unmet, HideAt(T(2, 2), T(6, 2), T(2, 0)).Rejected?.Code);
    }

    [Fact]
    public void A_downed_or_incapacitated_foe_sees_nothing()
    {
        var (state, rules) = Start(T(1, 3), T(2, 3));
        var downed = state with
        {
            Units = state.Units.SetItem(U("gob1"), state.Units[U("gob1")] with { Life = LifeState.Downed, Hp = 0, Countdown = 3 }),
        };

        Assert.Null(Battle.Check(downed, new Use(U("vex"), Hide, new Target()), rules));
    }

    [Fact]
    public void Hiding_ends_when_the_rogue_ends_its_turn_in_plain_view()
    {
        var (state, rules) = Start(T(2, 2), T(6, 2));
        state = Battle.Apply(state, new Use(U("vex"), Hide, new Target()), rules).State;
        state = Battle.Apply(state, new Move(U("vex"), [T(2, 2), T(2, 3)]), rules).State;
        Assert.True(IsHiding(state));   // walking through plain view doesn't end it: ending the turn there does

        var ended = Battle.Apply(state, new EndTurn(U("vex")), rules);

        Assert.Contains(new ConditionRemoved(U("vex"), Hiding), ended.Events);
        Assert.False(IsHiding(ended.State));
    }

    [Fact]
    public void Hiding_lasts_when_the_rogue_ends_its_turn_behind_cover()
    {
        var (state, rules) = Start(T(2, 2), T(6, 2));
        state = Battle.Apply(state, new Use(U("vex"), Hide, new Target()), rules).State;

        var ended = Battle.Apply(state, new EndTurn(U("vex")), rules);

        Assert.DoesNotContain(ended.Events, e => e is ConditionRemoved { Condition: var c } && c == Hiding);
        Assert.True(IsHiding(ended.State));
    }
}
