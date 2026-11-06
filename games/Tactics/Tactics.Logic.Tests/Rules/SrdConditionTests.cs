namespace Tactics.Logic.Tests;

public sealed class SrdConditionTests
{
    private static readonly UnitId Hero = new("hero");
    private static readonly UnitId Foe = new("foe");
    private static TestBattle Build() => TestBattle.On("0. 0. 0. 0. 0. 0.")
        .Party("hero", TestSheets.Fighter with { InitiativeBonus = 100 }, new Tile(0, 0))
        .Enemy("foe", TestSheets.Goblin with { MaxHp = 100 }, new Tile(1, 0))
        .WithConditions([.. BasePackTests.Rules.Conditions.Values]);

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 3, true)]
    public void Bleeding_ends_only_when_hp_is_restored(int missing, int healing, bool remains)
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("hero", u => u with { Hp = u.Sheet.MaxHp - missing })
            .Give("hero", TestConditions.Lasting(BasePackTests.Rules.Conditions[new("bleeding")], Foe));
        var (after, _) = battle.Run(state, "hero", "hero", null,
            new HealEffect(TestAbilities.Flat(healing.ToString()), []));
        Assert.Equal(remains, after.Units[Hero].Conditions.Any(c => c.Id.Value == "bleeding"));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(0, 0)]
    public void Standing_costs_half_starting_move_rounded_up_and_requires_movement(int move, int left)
    {
        var battle = TestBattle.On("0. 0. 0. 0. 0. 0.")
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 100, Move = move }, new Tile(0, 0))
            .Enemy("foe", TestSheets.Goblin with { MaxHp = 100 }, new Tile(5, 0))
            .WithConditions([.. BasePackTests.Rules.Conditions.Values]);
        var (state, _) = battle.Start();
        state = state.Give("hero", TestConditions.Lasting(BasePackTests.Rules.Conditions[new("prone")], Foe));
        (state, _) = battle.Apply(state, new EndTurn(Hero));
        (state, _) = battle.Apply(state, new EndTurn(Foe));
        Assert.Equal(left, state.Units[Hero].Economy.MoveLeft);
        Assert.Equal(move == 0, state.Units[Hero].Conditions.Any(c => c.Id.Value == "prone"));
    }

    [Fact]
    public void Charmed_cannot_attack_charmer_but_other_attacks_have_no_disadvantage()
    {
        var battle = TestBattle.On("0. 0. 0.\n0. 0. 0.")
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 100 }, new Tile(0, 0))
            .Enemy("foe", TestSheets.Goblin with { MaxHp = 100 }, new Tile(1, 0))
            .Enemy("other", TestSheets.Goblin with { MaxHp = 100 }, new Tile(0, 1))
            .WithConditions([.. BasePackTests.Rules.Conditions.Values]);
        var (state, _) = battle.Start();
        state = state.Give("hero", TestConditions.Lasting(BasePackTests.Rules.Conditions[new("charmed")], Foe));
        var rejected = Battle.Apply(state, new Use(Hero, new("common.attack"), new Target(Unit: Foe)), battle.Rules);
        Assert.NotNull(rejected.Rejected);
        Assert.Same(state, rejected.State);
        var (_, events) = battle.Apply(state, new Use(Hero, new("common.attack"), new Target(Unit: new("other"))));
        Assert.Single(events.OfType<Rolled>().First(e => e.Kind == RollKind.Attack).Result.D20s);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Put_out_flames_spends_action_on_self_or_adjacent_ally(bool self)
    {
        var action = BasePackTests.Rules.Abilities[new("common.extinguish")];
        var battle = TestBattle.On("0. 0. 0.\n0. 0. 0.")
            .Party("hero", TestSheets.Fighter with { InitiativeBonus = 100 }, new Tile(0, 0))
            .Party("ally", TestSheets.Fighter, new Tile(0, 1))
            .Enemy("foe", TestSheets.Goblin with { MaxHp = 100 }, new Tile(2, 0))
            .WithConditions([.. BasePackTests.Rules.Conditions.Values]).With(action);
        var (state, _) = battle.Start();
        var target = self ? Hero : new UnitId("ally");
        state = state.Give(target.Value, TestConditions.Lasting(BasePackTests.Rules.Conditions[new("burning")], Foe));
        var result = Battle.Apply(state, new Use(Hero, action.Id, new Target(Unit: target)), battle.Rules);
        Assert.Null(result.Rejected);
        Assert.DoesNotContain(result.State.Units[target].Conditions, c => c.Id.Value == "burning");
        Assert.False(result.State.Units[Hero].Economy.Action);
    }

    [Theory]
    [InlineData("common.shove", false)]
    [InlineData("common.shove_prone", true)]
    public void Shove_modes_use_better_strength_or_dexterity_save(string id, bool prone)
    {
        var ability = BasePackTests.Rules.Abilities[new(id)];
        var battle = Build().With(ability);
        var (state, _) = battle.Start();
        state = state.Change("foe", u => u with { Sheet = u.Sheet with
            { Scores = u.Sheet.Scores.SetItem(Stat.Str, 2).SetItem(Stat.Dex, 18), SaveProficiencies = [] } });
        var (_, events) = battle.Apply(state, new Use(Hero, ability.Id, new Target(Unit: Foe)));
        Assert.Equal(4, Assert.Single(events.OfType<Rolled>(), e => e.Kind == RollKind.Save).Result.Modifier);
        var save = Assert.IsType<SaveEffect>(Assert.Single(ability.Effects));
        Assert.Equal(prone, save.OnFail.Any(e => e is ApplyEffect a && a.Condition.Value == "prone"));
        Assert.Equal(!prone, save.OnFail.Any(e => e is PushEffect));
    }

    [Fact]
    public void Charmed_rejects_area_damage_that_would_catch_the_charmer()
    {
        var blast = BasePackTests.Rules.Abilities[new("wizard.fireball")] with
            { Id = new("common.test_blast"), Cost = new CostDef(Action: 1) };
        var battle = Build().With(blast);
        var (state, _) = battle.Start();
        state = state.Give("hero", TestConditions.Lasting(BasePackTests.Rules.Conditions[new("charmed")], Foe));
        var result = Battle.Apply(state, new Use(Hero, blast.Id, new Target(Tile: new Tile(2, 0))), battle.Rules);
        Assert.NotNull(result.Rejected);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Healing_a_downed_bleeding_unit_revives_it_and_ends_bleeding()
    {
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Change("hero", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 })
            .Give("hero", TestConditions.Lasting(BasePackTests.Rules.Conditions[new("bleeding")], Foe));
        var (after, _) = battle.Run(state, "foe", "hero", null, new HealEffect(TestAbilities.Flat("2"), []));
        Assert.Equal(LifeState.Up, after.Units[Hero].Life);
        Assert.Equal(2, after.Units[Hero].Hp);
        Assert.DoesNotContain(after.Units[Hero].Conditions, c => c.Id.Value == "bleeding");
    }

}
