namespace Tactics.Logic.Tests;

public sealed class SkipTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>kara, vex, gob in that order.</summary>
    private static TestBattle Build()
        => TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, T(0, 0))
            .Party("vex", TestSheets.Rogue, T(1, 0))
            .Enemy("gob", TestSheets.Goblin with { InitiativeBonus = -10 }, T(5, 1))
            .WithConditions([.. TestConditions.All]);

    [Fact]
    public void Downed_unit_turn_is_skipped_in_same_apply()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 5);
        Assert.Equal([U("kara"), U("vex"), U("gob")], state.Order);
        state = state.Change("vex", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 });

        var (after, events) = battle.Apply(state, new EndTurn(U("kara")));

        Assert.Equal(
            [new TurnEnded(U("kara")), new TurnSkipped(U("vex"), "downed"), new TurnStarted(U("gob"), 1)],
            events);
        Assert.Equal(U("gob"), after.Active);
    }

    [Fact]
    public void Stunned_unit_skipped_and_save_rolled_at_turn_end()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 5);
        var stun = new ActiveCondition(TestConditions.Stunned.Id, U("kara"), RoundsLeft: null, UntilSave: true, SaveDc: 1,
            Stat.Con, Concentration: false);
        state = state.Give("vex", stun);

        var (after, events) = battle.Apply(state, new EndTurn(U("kara")));

        Assert.Equal(new TurnSkipped(U("vex"), "incapacitated"), events[1]);
        var save = Assert.Single(events.OfType<Rolled>());
        Assert.Equal((U("vex"), RollKind.Save), (save.Roller, save.Kind));
        Assert.Contains(new ConditionSaved(U("vex"), TestConditions.Stunned.Id, save.Result), events);
        Assert.Empty(after.Units[U("vex")].Conditions);
        Assert.Equal(U("gob"), after.Active);
    }

    [Fact]
    public void Skipped_turn_still_ticks_the_conditions_its_unit_applied()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 5);
        // vex made kara invisible for 1 round earlier, and is now stunned (no save) with its turn coming up.
        state = state.Give("kara", TestConditions.Lasting(TestConditions.Invisible, U("vex"), rounds: 1))
            .Give("vex", TestConditions.Lasting(TestConditions.Stunned));

        var (after, events) = battle.Apply(state, new EndTurn(U("kara")));

        Assert.Contains(new TurnSkipped(U("vex"), "incapacitated"), events);
        Assert.Contains(new ConditionRemoved(U("kara"), TestConditions.Invisible.Id), events);
        Assert.Empty(after.Units[U("kara")].Conditions);
    }

    [Fact]
    public void Becoming_incapacitated_ends_concentration()
    {
        var stunBolt = new AbilityDef(new AbilityId("test.stun"), "Stun", Tier: 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new ApplyEffect(TestConditions.Stunned.Id, 1)]);
        var battle = TestBattle.On(Field)
            .Enemy("gob", (TestSheets.Goblin with { InitiativeBonus = 10 }).Knowing(stunBolt), T(5, 1))
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .WithConditions([.. TestConditions.All]).With(stunBolt);
        var (state, _) = battle.Start(seed: 5);
        state = state.Change("kara", u => u with { Concentrating = new AbilityId("test.bless") });

        var (after, events) = battle.Apply(state, new Use(U("gob"), stunBolt.Id, new Target(Unit: U("kara"))));

        Assert.Null(after.Units[U("kara")].Concentrating);
        Assert.Contains(new ConcentrationBroken(U("kara"), new AbilityId("test.bless")), events);
    }
}
