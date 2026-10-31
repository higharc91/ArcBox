namespace Tactics.Logic.Tests;

public sealed class InvariantTests
{
    private const string Field = """
        0. 0. 0# 0.
        0. 0x 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static (TestBattle Battle, BattleState State) Started()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Party("brin", TestSheets.Cleric, T(1, 0))
            .Enemy("gob", TestSheets.Goblin, T(3, 1));
        var (state, _) = battle.Start(seed: 2);
        return (battle, state);
    }

    private static void Throws(BattleState state, RulesPack rules, string fragment)
    {
        var e = Assert.Throws<RulesInvariantException>(() => Invariants.Check(state, rules));
        Assert.Contains(fragment, e.Message);
    }

    [Fact]
    public void Corrupted_state_throws()
    {
        var (battle, state) = Started();
        Invariants.Check(state, battle.Rules);

        var stacked = state.Change("brin", u => u with { Pos = T(0, 0) });
        Throws(stacked, battle.Rules, "(0, 0)");

        // Applying a command to it fails the check at the end of Apply rather than returning a broken state.
        Assert.Throws<RulesInvariantException>(() => Battle.Apply(stacked, new EndTurn(stacked.Active!.Value), battle.Rules));
    }

    [Fact]
    public void Each_rule_is_checked()
    {
        var (battle, s) = Started();
        var rules = battle.Rules;

        Throws(s.Change("kara", u => u with { Pos = T(2, 0) }), rules, "walkable");
        Throws(s.Change("kara", u => u with { Pos = T(9, 9) }), rules, "walkable");
        Throws(s.Change("kara", u => u with { Hp = 13 }), rules, "HP");
        Throws(s.Change("kara", u => u with { Hp = -1 }), rules, "HP");
        Throws(s.Change("kara", u => u with { Resources = u.Resources.SetItem("second_wind", -1) }), rules, "second_wind");
        Throws(s.Change("kara", u => u with { Economy = u.Economy with { MoveLeft = -1 } }), rules, "negative");
        Throws(s with { Order = s.Order.Remove(U("gob")) }, rules, "gob");
        Throws(s with { Order = s.Order.Add(U("kara")) }, rules, "kara");
        Throws(s with { TurnIndex = s.Order.Length }, rules, "TurnIndex");

        // An Up unit may stand in deep water (revived there). Downed units count for the order and occupancy but may lie
        // anywhere; Out and Dead ones count for neither.
        Invariants.Check(s.Change("kara", u => u with { Pos = T(1, 1) }), rules);
        Invariants.Check(s.Change("kara", u => u with { Life = LifeState.Downed, Hp = 0, Countdown = 2 }), rules);
        Invariants.Check(
            s.Change("gob", u => u with { Life = LifeState.Dead, Hp = 0, Pos = T(0, 0) }) with { Order = s.Order.Remove(U("gob")) },
            rules);
    }
}
