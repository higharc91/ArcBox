using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class MoveCommandTests
{
    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A fighter who acts first (initiative 19).</summary>
    private static readonly UnitSheet Fighter = TestSheets.Fighter with { InitiativeBonus = 10 };

    /// <summary>Events compare by JSON: records holding arrays compare their arrays by reference.</summary>
    private static string Json(ImmutableArray<BattleEvent> events) => JsonSerializer.Serialize(events, ArcJson.Options);

    private static Rejection Rejected(TestBattle battle, BattleState state, Command command)
    {
        var resolution = Battle.Apply(state, command, battle.Rules);
        Assert.Same(state, resolution.State);
        Assert.Empty(resolution.Events);
        return Assert.IsType<Rejection>(resolution.Rejected);
    }

    [Fact]
    public void Path_cost_spends_MoveLeft()
    {
        const string map = """
            0. 0^ 1. 3. 0. 0.
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(map)
            .Party("kara", Fighter, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2));
        var (state, _) = battle.Start();

        // Difficult (2) then a 1-level climb (1): 3 of 4 Move.
        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(0, 0), T(1, 0), T(2, 0)]));

        Assert.Equal(Json([new Moved(U("kara"), [T(0, 0), T(1, 0)]), new Moved(U("kara"), [T(1, 0), T(2, 0)])]), Json(events));
        var kara = after.Units[U("kara")];
        Assert.Equal(T(2, 0), kara.Pos);
        Assert.Equal(1, kara.Economy.MoveLeft);
        Assert.True(kara.Economy.Action);
        Assert.Empty(after.Queue);
        Assert.Equal(U("kara"), after.Active);

        // A 2-level climb costs 2: more than the 1 left.
        Assert.Equal(RejectCode.NoMove, Rejected(battle, after, new Move(U("kara"), [T(2, 0), T(3, 0)])).Code);
    }

    [Fact]
    public void Illegal_step_rejected_PathBlocked()
    {
        const string map = """
            0. 0. 0# 0. 0.
            0. 0. 0. 0. 0.
            0. 3. 0. 0. 0.
            """;
        var battle = TestBattle.On(map)
            .Party("kara", Fighter, T(0, 0))
            .Party("brin", TestSheets.Cleric, T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(3, 1));
        var (state, _) = battle.Start();
        Assert.Equal(U("kara"), state.Active);

        ImmutableArray<Tile>[] illegal =
        [
            [],                                                       // no path
            [T(0, 0)],                                                // no step
            [T(1, 0), T(1, 1)],                                       // doesn't start at the actor
            [T(0, 0), T(2, 0)],                                       // not adjacent
            [T(0, 0), T(1, 1)],                                       // diagonal
            [T(0, 0), T(-1, 0)],                                      // off the map
            [T(0, 0), T(1, 0), T(2, 0)],                              // impassable
            [T(0, 0), T(0, 1), T(0, 2), T(1, 2)],                     // a 3-level climb with Jump 2
            [T(0, 0), T(1, 0), T(1, 1), T(2, 1), T(3, 1)],            // through an enemy
            [T(0, 0), T(0, 1)],                                       // ends on an ally
        ];
        foreach (var path in illegal)
        {
            var rejection = Rejected(battle, state, new Move(U("kara"), path));
            Assert.Equal(RejectCode.PathBlocked, rejection.Code);
            Assert.Equal("You can't move there.", rejection.Reason);
        }

        // Allies can be walked through.
        var (after, _) = battle.Apply(state, new Move(U("kara"), [T(0, 0), T(0, 1), T(0, 2)]));
        Assert.Equal(T(0, 2), after.Units[U("kara")].Pos);
        Assert.Equal(2, after.Units[U("kara")].Economy.MoveLeft);
    }

    [Fact]
    public void Too_long_rejected_NoMove()
    {
        const string map = """
            0. 0. 0. 0. 0. 0.
            0. 0^ 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(map)
            .Party("kara", Fighter, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 1));
        var (state, _) = battle.Start();

        var fiveSteps = Rejected(battle, state, new Move(U("kara"), [T(0, 0), T(1, 0), T(2, 0), T(3, 0), T(4, 0), T(5, 0)]));
        Assert.Equal(RejectCode.NoMove, fiveSteps.Code);
        Assert.Equal("You don't have enough movement.", fiveSteps.Reason);

        // Four steps, but one is difficult: 5 Move.
        var difficult = Rejected(battle, state, new Move(U("kara"), [T(0, 0), T(0, 1), T(1, 1), T(2, 1), T(3, 1)]));
        Assert.Equal(RejectCode.NoMove, difficult.Code);

        // An illegal step wins over the cost.
        var both = Rejected(battle, state, new Move(U("kara"), [T(0, 0), T(1, 0), T(2, 0), T(3, 0), T(4, 0), T(6, 0)]));
        Assert.Equal(RejectCode.PathBlocked, both.Code);
    }

    [Fact]
    public void Split_move_before_and_after_action()
    {
        const string map = """
            0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0.
            """;
        var bolt = TestAbilities.GuidingBolt;
        var battle = TestBattle.On(map)
            .Party("cleric", TestSheets.Cleric with { Abilities = [bolt.Id], InitiativeBonus = 5 }, T(0, 1))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(7, 1))
            .With(bolt);
        var (state, _) = battle.Start();

        (state, _) = battle.Apply(state, new Move(U("cleric"), [T(0, 1), T(1, 1), T(2, 1)]));
        Assert.Equal(2, state.Units[U("cleric")].Economy.MoveLeft);

        (state, _) = battle.Apply(state, new Use(U("cleric"), bolt.Id, new Target(Unit: U("gob"))));
        Assert.False(state.Units[U("cleric")].Economy.Action);
        Assert.Equal(2, state.Units[U("cleric")].Economy.MoveLeft);

        (state, _) = battle.Apply(state, new Move(U("cleric"), [T(2, 1), T(2, 0), T(3, 0)]));
        var cleric = state.Units[U("cleric")];
        Assert.Equal(T(3, 0), cleric.Pos);
        Assert.Equal(0, cleric.Economy.MoveLeft);

        Assert.Equal(RejectCode.NoMove, Rejected(battle, state, new Move(U("cleric"), [T(3, 0), T(4, 0)])).Code);
    }
}
