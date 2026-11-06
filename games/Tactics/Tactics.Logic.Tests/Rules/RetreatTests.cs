namespace Tactics.Logic.Tests;

public sealed class RetreatTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static (TestBattle Battle, BattleState State) Fight()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, new Tile(2, 1))
            .Party("comp", TestSheets.Cleric, new Tile(0, 0), new AiControlled("companion"))
            .Enemy("gob", TestSheets.Goblin, new Tile(5, 1));
        var (state, _) = battle.Start(seed: 4);
        return (battle, state with { Xp = 251 });
    }

    [Fact]
    public void A_retreat_from_round_three_ends_the_battle_Fled_for_half_the_xp_and_no_loot()
    {
        var (battle, state) = Fight();
        state = state with { Round = 3, Loot = [new LootTableId("goblin_cache")] };

        var (after, events) = battle.Apply(state, new Retreat(U("kara")));

        Assert.Equal(BattleOutcome.Fled, after.Outcome);
        Assert.Equal(new BattleEnded(BattleOutcome.Fled), events[^1]);
        Assert.Empty(after.Queue);
        var result = Battle.Result(after, battle.Rules)!;
        Assert.Equal(BattleOutcome.Fled, result.Outcome);
        Assert.Equal([125, 125], result.Xp.Values);
        Assert.Empty(result.Loot);
    }

    [Fact]
    public void A_retreat_is_refused_before_round_three()
    {
        var (battle, state) = Fight();
        foreach (var round in new[] { 1, 2 })
        {
            var resolution = Battle.Apply(state with { Round = round }, new Retreat(U("kara")), battle.Rules);
            Assert.Equal(RejectCode.CantRetreat, resolution.Rejected?.Code);
            Assert.Null(resolution.State.Outcome);
        }
    }

    [Fact]
    public void A_retreat_is_refused_when_the_encounter_forbids_it()
    {
        var (battle, state) = Fight();

        var resolution = Battle.Apply(state with { Round = 5, NoRetreat = true }, new Retreat(U("kara")), battle.Rules);

        Assert.Equal(RejectCode.CantRetreat, resolution.Rejected?.Code);
        Assert.Null(resolution.State.Outcome);
    }

    [Fact]
    public void Any_party_unit_may_retreat_on_anothers_turn_but_not_an_enemy_or_a_stranger()
    {
        var (battle, state) = Fight();
        state = state with { Round = 4 };

        Assert.Equal(RejectCode.InvalidTarget, Battle.Apply(state, new Retreat(U("gob")), battle.Rules).Rejected?.Code);
        Assert.Equal(RejectCode.InvalidTarget, Battle.Apply(state, new Retreat(U("nobody")), battle.Rules).Rejected?.Code);
        Assert.Null(Battle.Apply(state, new Retreat(U("comp")), battle.Rules).Rejected);
    }

    [Fact]
    public void A_retreat_after_the_end_is_refused_BattleOver()
    {
        var (battle, state) = Fight();
        var (ended, _) = battle.Apply(state with { Round = 3 }, new Retreat(U("kara")));

        Assert.Equal(RejectCode.BattleOver, Battle.Apply(ended, new Retreat(U("kara")), battle.Rules).Rejected?.Code);
    }
}
