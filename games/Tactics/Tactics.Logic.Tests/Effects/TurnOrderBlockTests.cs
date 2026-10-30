namespace Tactics.Logic.Tests;

public sealed class TurnOrderBlockTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>Round 1's order: kara (19), gob (12), hob (12), cleric (10).</summary>
    private static TestBattle Build()
        => TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(4, 0))
            .Enemy("hob", TestSheets.Goblin, T(5, 0))
            .Party("cleric", TestSheets.Cleric, T(0, 1));

    [Fact]
    public void TurnOrder_swap_changes_this_round()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        Assert.Equal([U("kara"), U("gob"), U("hob"), U("cleric")], state.Order);

        var (after, events) = battle.Run(state, "kara", "gob", null, new TurnOrderEffect("swap", 1));
        Assert.Equal<BattleEvent>([new TurnOrderChanged([U("kara"), U("hob"), U("gob"), U("cleric")])], events,
            new JsonEquality<BattleEvent>());
        Assert.Equal([U("kara"), U("hob"), U("gob"), U("cleric")], after.Order);

        // This round: hob goes next. The next round is sorted by initiative again.
        (after, events) = battle.Apply(after, new EndTurn(U("kara")));
        Assert.Equal(new TurnStarted(U("hob"), 1), events[^1]);
        (after, _) = battle.Apply(after, new EndTurn(U("hob")));
        (after, _) = battle.Apply(after, new EndTurn(U("gob")));
        (after, _) = battle.Apply(after, new EndTurn(U("cleric")));
        Assert.Equal([U("kara"), U("gob"), U("hob"), U("cleric")], after.Order);
    }

    [Fact]
    public void Delay_and_hasten_move_a_waiting_unit()
    {
        var battle = Build();
        var (state, _) = battle.Start();

        var (after, _) = battle.Run(state, "kara", "gob", null, new TurnOrderEffect("delay", 2));
        Assert.Equal([U("kara"), U("hob"), U("cleric"), U("gob")], after.Order);
        (after, _) = battle.Run(after, "kara", "gob", null, new TurnOrderEffect("delay", 5));
        Assert.Equal([U("kara"), U("hob"), U("cleric"), U("gob")], after.Order);   // already last

        // Hasten never moves a unit ahead of the next turn.
        (after, _) = battle.Run(after, "kara", "gob", null, new TurnOrderEffect("hasten", 9));
        Assert.Equal([U("kara"), U("gob"), U("hob"), U("cleric")], after.Order);
        Assert.Equal(0, after.TurnIndex);
    }

    [Fact]
    public void A_unit_whose_turn_has_come_this_round_stays_put()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal(U("gob"), state.Active);

        foreach (var unit in new[] { "kara", "gob" })
        foreach (var op in new[] { "delay", "hasten", "swap" })
        {
            var (after, events) = battle.Run(state, "gob", unit, null, new TurnOrderEffect(op, 1));
            Assert.Empty(events);
            Assert.Equal(state.Order, after.Order);
        }
    }
}
