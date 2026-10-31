using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary><see cref="Choices.For"/>: what a phone offers on a turn is exactly what the engine accepts.</summary>
public sealed class ChoicesTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly string[] Classes =
        ["barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard"];

    /// <summary>
    /// Every class at levels 1, 5 and 10 on the river crossing, played by the AI until each member's first turn: every move
    /// and every target offered is accepted.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void Every_offered_option_is_accepted(int level)
    {
        foreach (var chunk in Classes.Chunk(6))
        {
            ImmutableArray<PartyMember> party = [.. chunk.Select(cls => new PartyMember(U(cls),
                SheetBuilder.DefaultCharacter(cls, "human", cls, null, level, Base) with
                {
                    Bag = [new ItemStack(new ItemId("potion_of_healing"), 1)],
                },
                new PlayerControlled(new ArcBox.Sdk.ProfileId("p_" + cls))))];
            var setup = Encounters.Setup("river_crossing", party, Difficulty.Normal, Base);
            var state = Battle.Start(setup, Base, seed: 7).State;
            var seen = new HashSet<UnitId>();
            var moved = 0;
            for (var guard = 0; guard < 200 && state.Outcome is null && seen.Count < party.Length; guard++)
            {
                var active = state.Active!.Value;
                if (state.Units[active].Controller is PlayerControlled && seen.Add(active))
                    moved += CheckAll(state, active) > 0 ? 1 : 0;
                state = PlayTurn(state, active);
            }
            Assert.Equal(party.Length, seen.Count);
            Assert.True(moved > 0, "No member was offered a move.");
        }
    }

    /// <summary>Applies every option offered to <paramref name="unit"/>, each to the same state; returns how many moves there were.</summary>
    private static int CheckAll(BattleState state, UnitId unit)
    {
        var choices = Choices.For(state, unit, Base);
        Assert.NotEmpty(choices.Abilities);
        foreach (var move in choices.Moves)
            Assert.Null(Battle.Apply(state, new Move(unit, move.Path), Base).Rejected);
        foreach (var ability in choices.Abilities)
        foreach (var target in ability.Targets)
        {
            var resolution = Battle.Apply(state, new Use(unit, ability.Ability, target.Target, ability.Item), Base);
            Assert.True(resolution.Rejected is null, $"{unit.Value}: {ability.Ability.Value} at {target.Target} was refused: {resolution.Rejected?.Reason}");
        }
        return choices.Moves.Length;
    }

    /// <summary>The AI plays the active unit's whole turn, answering any player reaction with a pass.</summary>
    private static BattleState PlayTurn(BattleState state, UnitId unit)
    {
        foreach (var command in Ai.Plan(state, unit, Base, new AiOptions(ProfileOverride: "companion")).Commands)
        {
            var resolution = Battle.Apply(state, command, Base);
            if (resolution.Rejected is not null)
                break;
            state = resolution.State;
            while (state.Pending is ReactionOffer offer)
                state = Battle.Apply(state, new React(offer.Reactor, offer.Options[0]), Base).State;
            if (state.Outcome is not null || state.Active != unit)
                return state;
        }
        return state.Outcome is null && state.Active == unit ? Battle.Apply(state, new EndTurn(unit), Base).State : state;
    }

    [Fact]
    public void A_fighter_next_to_a_goblin_can_attack_it_and_walk_round_it()
    {
        var battle = TestBattle.On("""
            0. 0. 0. 0.
            0. 0. 0. 0.
            """)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 20 }, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(1, 0));
        var (state, _) = battle.Start();

        var choices = Choices.For(state, U("kara"), battle.Rules);

        var attack = Assert.Single(choices.Abilities, a => a.Ability.Value == "common.attack");
        var onGoblin = Assert.Single(attack.Targets);
        Assert.Equal(U("gob"), onGoblin.Target.Unit);
        Assert.True(Assert.Single(onGoblin.Affected).Damage > 0);
        Assert.DoesNotContain(choices.Moves, m => m.To == T(1, 0));
        Assert.Equal(3, Assert.Single(choices.Moves, m => m.To == T(2, 1)).Cost);
        Assert.True(choices.CanDelay);
    }

    [Fact]
    public void Nothing_is_offered_out_of_turn()
    {
        var battle = TestBattle.On("""
            0. 0. 0. 0.
            """)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", TestSheets.Goblin with { InitiativeBonus = 20 }, T(3, 0));
        var (state, _) = battle.Start();

        var choices = Choices.For(state, U("kara"), battle.Rules);

        Assert.Empty(choices.Moves);
        Assert.Empty(choices.Abilities);
        Assert.False(choices.CanDelay);
    }
}
