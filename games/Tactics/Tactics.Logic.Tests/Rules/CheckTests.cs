namespace Tactics.Logic.Tests;

/// <summary><see cref="Battle.Check"/>: Apply's verdict on a command, with nothing resolved.</summary>
public sealed class CheckTests
{
    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    [Fact]
    public void Check_gives_the_verdict_Apply_gives()
    {
        var battle = TestBattle.On("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(1, 0));
        var (state, _) = battle.Start();

        Command[] commands =
        [
            new Move(U("kara"), [T(0, 0), T(0, 1), T(1, 1)]),                              // accepted
            new Move(U("kara"), [T(0, 0), T(1, 0)]),                                       // onto the goblin
            new Use(U("kara"), RulesPack.CommonAttack.Id, new Target(Unit: U("gob"))),    // accepted
            new Use(U("kara"), RulesPack.CommonAttack.Id, new Target(Unit: U("kara"))),   // not an enemy
            new EndTurn(U("gob")),                                                         // not its turn
            new React(U("kara"), new ReactionChoice(Reactions.Pass, null, null)),          // nothing to react to
            new EndTurn(U("kara")),
        ];
        foreach (var command in commands)
        {
            var check = Battle.Check(state, command, battle.Rules);
            Assert.Equal(Battle.Apply(state, command, battle.Rules).Rejected, check);
        }
        Assert.Null(Battle.Check(state, commands[0], battle.Rules));
        Assert.Equal(RejectCode.PathBlocked, Battle.Check(state, commands[1], battle.Rules)?.Code);
        Assert.Equal(RejectCode.InvalidTarget, Battle.Check(state, commands[3], battle.Rules)?.Code);
        Assert.Equal(RejectCode.NotYourTurn, Battle.Check(state, commands[4], battle.Rules)?.Code);
        Assert.Equal(RejectCode.NothingToReact, Battle.Check(state, commands[5], battle.Rules)?.Code);
    }
}
