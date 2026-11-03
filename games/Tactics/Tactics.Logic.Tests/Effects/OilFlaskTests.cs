using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class OilFlaskTests
{
    private static readonly ItemId Oil = new("oil_flask");
    private static readonly ConditionId Oiled = new("oiled");
    private static UnitId U(string id) => new(id);
    private static Tile T(int x, int y) => new(x, y);
    private static RulesPack Pack => BasePackTests.Rules;
    private static UnitSheet Tough => TestSheets.Fighter with { MaxHp = 100, Scores = TestSheets.Fighter.Scores.SetItem(Stat.Dex, -30) };
    private static TestBattle Build(bool enabled = true, UnitSheet? victim = null)
    {
        Assert.Contains(Oil, Pack.Items.Keys);
        Assert.Contains(Oiled, Pack.Conditions.Keys);
        var ability = Pack.Abilities[Pack.Items[Oil].Use];
        return TestBattle.On("0. 0. 0. 0. 0.\n0. 0. 0. 0. 0.\n0. 0. 0. 0. 0.")
            .WithEnvironment(Pack.Environment, enabled).With(ability, Pack.Abilities[Battle.UseItem])
            .WithItems(Pack.Items[Oil]).WithConditions(Pack.Conditions[Oiled])
            .Party("mage", TestSheets.Cleric with { MaxHp = 100, InitiativeBonus = 100,
                Scores = TestSheets.Cleric.Scores.SetItem(Stat.Dex, 14).SetItem(Stat.Wis, 30) }, T(0, 0), bag: [new ItemStack(Oil, 1)])
            .Party("friend", Tough, T(1, 1)).Enemy("foe", victim ?? Tough, T(2, 1))
            .Enemy("saved", Tough with { Scores = Tough.Scores.SetItem(Stat.Dex, 100) }, T(3, 1));
    }
    private static Use Throw(bool unit = false) => new(U("mage"), Battle.UseItem, unit ? new Target(Unit: U("foe")) : new Target(Tile: T(2, 1)), Oil);
    private static DamageEffect Damage(string amount, string type) => new(TestAbilities.Flat(amount), type, []);
    private static BattleState Coat(TestBattle battle) => battle.Run(battle.Start().State, "mage", "foe", T(2, 1), new ApplyEffect(Oiled, 10)).State;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Throwing_splashes_five_tiles_and_all_sides_use_the_throwers_Dex_DC(bool unit)
    {
        var battle = Build();
        var result = battle.Apply(battle.Start().State, Throw(unit));
        var oil = Assert.Single(result.State.Zones);
        Assert.Equal("oil", oil.Kind); Assert.True(oil.Surface); Assert.False(oil.Difficult);
        Assert.Equal(10, oil.RoundsLeft); Assert.Equal(U("mage"), oil.Owner); Assert.Equal("oil", oil.Source);
        Assert.Equal(new[] { T(1, 1), T(2, 0), T(2, 1), T(2, 2), T(3, 1) }, oil.Tiles);
        Assert.All(result.Events.OfType<Rolled>(), e => Assert.Equal(12, e.Result.Vs));
        Assert.Equal(3, result.Events.OfType<Rolled>().Count());
        Assert.Contains(result.State.Units[U("friend")].Conditions, c => c.Id == Oiled);
        Assert.Contains(result.State.Units[U("foe")].Conditions, c => c.Id == Oiled);
        Assert.DoesNotContain(result.State.Units[U("saved")].Conditions, c => c.Id == Oiled);
        Assert.Empty(result.State.Units[U("mage")].Bag); Assert.False(result.State.Units[U("mage")].Economy.Action);
    }

    [Fact]
    public void Disabled_encounters_reject_oil_before_spending_it()
    {
        var battle = Build(enabled: false); var state = battle.Start().State;
        var result = Battle.Apply(state, Throw(), battle.Rules);
        Assert.NotNull(result.Rejected); Assert.Same(state, result.State); Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData("normal", 8)] [InlineData("resist", 4)] [InlineData("vulnerable", 16)] [InlineData("immune", 0)]
    public void Oil_adds_five_to_one_fire_packet_before_resistance_and_consumes_once(string defense, int damage)
    {
        var sheet = Tough with { Resist = defense == "resist" ? Ordinal.Set("fire") : Ordinal.Set(),
            Immune = defense == "immune" ? Ordinal.Set("fire") : Ordinal.Set(),
            Vulnerable = defense == "vulnerable" ? Ordinal.Set("fire") : Ordinal.Set() };
        var battle = Build(victim: sheet); var state = Coat(battle);
        state = battle.Run(state, "mage", "foe", T(2, 1), Damage("2", "cold")).State;
        Assert.Contains(state.Units[U("foe")].Conditions, c => c.Id == Oiled);
        var result = battle.Run(state, "mage", "foe", T(2, 1), Damage("3", "fire"));
        Assert.Equal(damage, Assert.Single(result.Events.OfType<Damaged>()).Amount);
        Assert.Equal(defense == "immune", result.State.Units[U("foe")].Conditions.Any(c => c.Id == Oiled));
        if (defense != "immune")
        {
            result = battle.Run(result.State, "mage", "foe", T(2, 1), Damage("3", "fire"));
            Assert.Equal(defense == "resist" ? 1 : defense == "vulnerable" ? 6 : 3, Assert.Single(result.Events.OfType<Damaged>()).Amount);
        }
    }

    [Fact]
    public void The_bonus_can_make_the_first_hit_lethal_and_keeps_the_fire_attackers_credit()
    {
        var reward = new PassiveDef("test.kill", HookKind.OnKill, null,
            [new TempHpEffect(TestAbilities.Flat("2"), [])], null, Target: PassiveTarget.Self);
        var battle = Build(victim: Tough with { MaxHp = 7 }).WithPassives(reward);
        var state = Coat(battle).Change("friend", u => u with { Sheet = u.Sheet with { Passives = [reward.Id] } });
        var result = battle.Run(state, "friend", "foe", T(2, 1), Damage("3", "fire"));
        Assert.Equal(LifeState.Dead, result.State.Units[U("foe")].Life);
        Assert.Equal(8, Assert.Single(result.Events.OfType<Damaged>()).Amount);
        Assert.DoesNotContain(result.State.Units[U("foe")].Conditions, c => c.Id == Oiled);
        Assert.Equal(2, result.State.Units[U("friend")].TempHp);
        Assert.Equal(0, result.State.Units[U("mage")].TempHp);
    }

    [Fact]
    public void Oil_and_coatings_round_trip_then_expire_on_their_own_clocks()
    {
        var battle = Build(); var state = battle.Apply(battle.Start().State, Throw()).State;
        var json = JsonSerializer.Serialize(state, ArcJson.Options);
        state = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(state, ArcJson.Options));
        for (var n = 0; state.Round < 11 && n < 100; n++)
            state = battle.Apply(state, new EndTurn(state.Active!.Value)).State;
        Assert.Equal(11, state.Round);
        Assert.DoesNotContain(state.Zones, z => z.Kind == "oil");
        // Conditions use the existing source-turn clock: the cast turn does not spend a full round.
        state = battle.Apply(state, new EndTurn(state.Active!.Value)).State;
        Assert.All(state.Units.Values, u => Assert.DoesNotContain(u.Conditions, c => c.Id == Oiled));
    }

    [Fact]
    public void Zero_fire_damage_does_not_consume_the_coating()
    {
        var battle = Build();
        var result = battle.Run(Coat(battle), "mage", "foe", T(2, 1), Damage("0", "fire"));
        Assert.Equal(0, Assert.Single(result.Events.OfType<Damaged>()).Amount);
        Assert.Contains(result.State.Units[U("foe")].Conditions, c => c.Id == Oiled);
    }
    [Theory]
    [InlineData("single", 8)] [InlineData("two", 11)] [InlineData("repeat", 14)] [InlineData("attack", 11.7)]
    [InlineData("save", 11)] [InlineData("half", 9)] [InlineData("random", 11)]
    public void Damage_previews_apply_the_coating_bonus_once_per_use_including_misses(string mode, double expected)
    {
        var fire = Damage("3", "fire");
        ImmutableArray<EffectDef> effects = mode switch
        {
            "two" => [fire, fire],
            "repeat" => [new RepeatEffect(3, [fire])],
            "attack" => [new RepeatEffect(3, [new AttackEffect("spell", null, [fire], [], [])])],
            "save" or "half" => [new SaveEffect(Stat.Dex, 10, [fire], [], HalfOnSuccess: true), fire],
            "random" => [new RandomEffect([new RandomEntry(1, [fire]), new RandomEntry(1, [Damage("3", "cold")])]), fire],
            _ => [fire],
        };
        var spell = new AbilityDef(new AbilityId("test.fire"), "Fire", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 4, false, new AreaDef(AreaShape.Single)), effects);
        var battle = Build(victim: mode == "half" ? Tough with { Scores = Tough.Scores.SetItem(Stat.Dex, 100) } : null).With(spell);
        var state = Coat(battle).Change("mage", u => u with { Sheet = u.Sheet with { Abilities = u.Sheet.Abilities.Add(spell.Id) } });
        var choice = Assert.Single(Choices.For(state, U("mage"), battle.Rules).Abilities, a => a.Ability == spell.Id);
        var target = Assert.Single(choice.Targets, t => t.Target.Unit == U("foe"));
        Assert.Equal(expected, Assert.Single(target.Affected).Damage, precision: 8);
    }

    [Fact]
    public void AI_does_not_invent_a_lethal_double_bonus_from_two_fire_hits()
    {
        var fire = new AbilityDef(new AbilityId("test.rays"), "Two rays", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 4, false, new AreaDef(AreaShape.Single)),
            [new RepeatEffect(2, [new AttackEffect("spell", null, [Damage("3", "fire")], [], [])])]);
        var force = fire with { Id = new AbilityId("test.force"), Effects = [Damage("10", "force")] };
        var battle = Build(victim: Tough with { MaxHp = 14 }).With(fire, force)
            .WithAiProfiles([Pack.AiProfiles["caster"] with { Id = "test.kill", Kill = 100 }]);
        var state = Coat(battle).Change("mage", u => u with { Sheet = u.Sheet with { Abilities = [fire.Id, force.Id], Move = 0 },
            Economy = u.Economy with { MoveLeft = 0 } });
        // Two successful rays can deal at most 3 + 3 + 5 = 11. They cannot kill this 14-HP foe.
        var plan = Ai.Plan(state, U("mage"), battle.Rules, new AiOptions(ProfileOverride: "test.kill"));
        Assert.Equal(force.Id, Assert.Single(plan.Commands.OfType<Use>()).Ability);
    }

}
