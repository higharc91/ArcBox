using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>The objective leaves of spec §4.8 and how they decide a battle.</summary>
public sealed class ObjectiveTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static ObjectivesDef Win(ObjectiveExpr win) => new(win, ObjectiveExpr.Of(Objectives.PartyOut));

    private static ObjectiveExpr Leaf(string leaf) => ObjectiveExpr.Of(leaf);

    private static UnitSheet Tagged(UnitSheet sheet, string tag) => sheet with { Tags = sheet.Tags.Add(tag) };

    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 20 };

    [Fact]
    public void A_transformed_unit_keeps_its_tags()
    {
        var elite = Tagged(TestSheets.Goblin, "elite");
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("a", elite, T(3, 1))
            .Enemy("b", elite, T(5, 2))
            .WithObjectives(Win(Leaf("defeat:elite")));
        var (state, _) = battle.Start(seed: 4);

        var polymorphed = state.Change("a", u => u with { Life = LifeState.Dead, Hp = 0 })
            .Change("b", u => u with { Sheet = TestSheets.Goblin, TrueSheet = u.Sheet });

        Assert.Null(Objectives.Decide(polymorphed));   // b is still an elite, in the battle
    }

    [Fact]
    public void Defeat_tag_wins()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter).Knowing(DownedTests.Smite), T(2, 1))
            .Enemy("boss", Tagged(TestSheets.Goblin, "boss"), T(3, 1))
            .Enemy("minion", TestSheets.Goblin, T(5, 2))
            .With(DownedTests.Smite)
            .WithObjectives(Win(Leaf("defeat:boss")));
        var (state, _) = battle.Start(seed: 4);
        state = state.Change("boss", u => u with { Hp = 1 });
        Assert.Null(Objectives.Decide(state));

        var (after, events) = battle.Apply(state, new Use(U("kara"), DownedTests.Smite.Id, new Target(Unit: U("boss"))));

        Assert.Equal(BattleOutcome.Won, after.Outcome);
        Assert.Equal(new BattleEnded(BattleOutcome.Won), events[^1]);
        Assert.Equal(LifeState.Up, after.Units[U("minion")].Life);   // only the tagged unit had to fall
    }

    [Fact]
    public void Survive_8_wins_at_round_9_start()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithObjectives(Win(Leaf("survive:8")));
        var (state, _) = battle.Start(seed: 2);
        state = state with { Round = 8 };
        Assert.False(Objectives.Leaf(state, "survive:8"));

        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Null(state.Outcome);   // still round 8: the goblin's turn
        var (after, events) = battle.Apply(state, new EndTurn(U("gob")));

        Assert.Equal(9, after.Round);
        Assert.Equal(BattleOutcome.Won, after.Outcome);
        var started = events.Select((e, i) => (e, i)).Single(p => p.e is RoundStarted { Round: 9 }).i;
        var ended = events.Select((e, i) => (e, i)).Single(p => p.e is BattleEnded).i;
        Assert.True(started < ended);
        Assert.DoesNotContain(events.Skip(started), e => e is TurnStarted);   // round 9 never got going
    }

    [Fact]
    public void Reach_exit_with_2_wins()
    {
        var map = AsciiMap.Parse(Field, Ordinal.Map(("exit", ImmutableArray.Create(T(5, 0), T(5, 1)))));
        var battle = TestBattle.On(map)
            .Party("kara", First(TestSheets.Fighter), T(4, 0))
            .Party("vex", TestSheets.Rogue, T(5, 1))
            .Enemy("gob", TestSheets.Goblin, T(0, 2))
            .WithObjectives(Win(Leaf("reach:exit:2")));
        var (state, _) = battle.Start(seed: 3);
        Assert.Null(state.Outcome);   // one on the exit isn't enough
        Assert.False(Objectives.Leaf(state, "reach:exit:2", map));
        Assert.True(Objectives.Leaf(state, "reach:exit:1", map));

        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(4, 0), T(5, 0)]));

        Assert.Equal(BattleOutcome.Won, after.Outcome);
        Assert.Equal(new BattleEnded(BattleOutcome.Won), events[^1]);
    }

    [Fact]
    public void Protect_npc_out_loses()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Npc("mayor", Tagged(TestSheets.Cleric, "mayor"), T(1, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithObjectives(new ObjectivesDef(
                new ObjectiveExpr(null, [], [Leaf("survive:3"), Leaf("protect:mayor")]),
                ObjectiveExpr.Of(Objectives.PartyOut)));
        var (state, _) = battle.Start(seed: 5);
        Assert.True(Objectives.Leaf(state, "protect:mayor"));

        // Downed still counts as protected; Out is a loss, whatever the lose expression says.
        var downed = state.Change("mayor", u => u with { Life = LifeState.Downed, Hp = 0, Countdown = 3 });
        Assert.True(Objectives.Leaf(downed, "protect:mayor"));
        Assert.Null(Objectives.Decide(downed));

        var dying = state.Change("mayor", u => u with { Life = LifeState.Downed, Hp = 0, Countdown = 1 });
        (dying, _) = battle.Apply(dying, new EndTurn(U("kara")));
        var (after, events) = battle.Apply(dying, new EndTurn(U("gob")));   // the new round ticks the countdown to 0

        Assert.Contains(events, e => e is UnitOut { Unit.Value: "mayor" });
        Assert.False(Objectives.Leaf(after, "protect:mayor"));
        Assert.Equal(BattleOutcome.Lost, after.Outcome);
        Assert.Equal(LifeState.Up, after.Units[U("kara")].Life);   // the party itself is fine
    }

    [Fact]
    public void Any_of_defeat_or_survive()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("boss", Tagged(TestSheets.Goblin, "boss"), T(5, 2))
            .Enemy("minion", TestSheets.Goblin, T(5, 0))
            .WithObjectives(Win(new ObjectiveExpr(null, [Leaf("defeat:boss"), Leaf("survive:8")], [])));
        var (state, _) = battle.Start(seed: 6);

        Assert.Null(Objectives.Decide(state));
        Assert.Equal(BattleOutcome.Won, Objectives.Decide(state with { Round = 9 }));
        Assert.Equal(BattleOutcome.Won, Objectives.Decide(state.Change("boss", u => u with { Life = LifeState.Dead, Hp = 0 })));
        Assert.Null(Objectives.Decide(state.Change("minion", u => u with { Life = LifeState.Dead, Hp = 0 })));
        // A tag nobody has ever carried isn't "defeated".
        Assert.False(Objectives.Leaf(state, "defeat:ghosts"));
    }

    [Fact]
    public void Flag_objective()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", First(TestSheets.Fighter), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(5, 2))
            .WithObjectives(Win(Leaf("flag:bridge_down")));
        var (state, _) = battle.Start(seed: 7);

        Assert.Null(Objectives.Decide(state));
        Assert.Equal(BattleOutcome.Won, Objectives.Decide(state with { Flags = state.Flags.Add("bridge_down") }));
        Assert.Null(Objectives.Decide(state with { Flags = state.Flags.Add("bridge_up") }));
    }
}
