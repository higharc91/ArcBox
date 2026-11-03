using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class EnvironmentalAiTests
{
    private static Tile T(int x, int y) => new(x, y);
    private static UnitId U(string id) => new(id);
    private static AbilityDef Bolt(string id, string type, string amount, int range = 4) => new(new AbilityId(id), id, 0,
        new CostDef(Action: 1), new TargetDef(TargetKind.Tile, range, false, new AreaDef(AreaShape.Single)),
        [new DamageEffect(TestAbilities.Flat(amount), type, [])]);
    private static readonly AbilityDef Lightning = Bolt("test.lightning", "lightning", "8");
    private static readonly AbilityDef Force = Bolt("test.force", "force", "10");
    private static readonly UnitSheet Tough = TestSheets.Fighter with { MaxHp = 100, Move = 0 };
    private static UnitSheet Caster(params AbilityDef[] abilities) => (TestSheets.Cleric with { MaxHp = 100, Move = 0, InitiativeBonus = 100 }).Knowing(abilities);
    private static TestBattle On(string map, bool enabled = true) => TestBattle.On(map).WithEnvironment(BasePackTests.Rules.Environment, enabled)
        .WithAiProfiles(BasePackTests.Rules.AiProfiles.Values)
        .WithConditions(BasePackTests.Rules.Conditions[new ConditionId("prone")]);
    private static Use? Pick(TestBattle battle, BattleState state) => Ai.Plan(state, U("mage"), battle.Rules,
        new AiOptions(ProfileOverride: "caster")).Commands.OfType<Use>().FirstOrDefault();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Lightning_is_chosen_for_two_conducted_foes_only_when_enabled(bool enabled)
    {
        var battle = On("0. 0~ 0~ 0~ 0~", enabled).With(Lightning, Force)
            .Party("mage", Caster(Lightning, Force), T(0, 0)).Enemy("one", Tough, T(2, 0)).Enemy("two", Tough, T(3, 0));
        Assert.Equal(enabled ? Lightning.Id : Force.Id, Pick(battle, battle.Start().State)!.Ability);
    }

    [Theory]
    [InlineData(Difficulty.Story)] [InlineData(Difficulty.Normal)] [InlineData(Difficulty.Hard)]
    public void Story_forbids_conducted_ally_damage_even_when_the_net_value_is_positive(Difficulty difficulty)
    {
        var force = Force with { Effects = [new DamageEffect(TestAbilities.Flat("1"), "force", [])] };
        var battle = On("0. 0~ 0~ 0~ 0~ 0~").With(Lightning, force)
            .Party("mage", Caster(Lightning, force), T(0, 0)).Party("friend", Tough, T(1, 0))
            .Enemy("one", Tough, T(2, 0)).Enemy("two", Tough, T(3, 0)).Enemy("three", Tough, T(4, 0));
        var state = battle.Start().State with { Difficulty = difficulty };
        Assert.Equal(difficulty == Difficulty.Story ? force.Id : Lightning.Id, Pick(battle, state)!.Ability);
    }

    [Fact]
    public void Lightning_can_aim_at_empty_water_when_the_foes_are_outside_direct_range()
    {
        var bolt = Lightning with { Target = Lightning.Target with { Range = 1 } };
        var battle = On("0. 0~ 0~ 0~ 0~").With(bolt).Party("mage", Caster(bolt), T(0, 0))
            .Enemy("one", Tough, T(3, 0)).Enemy("two", Tough, T(4, 0));
        var use = Pick(battle, battle.Start().State);
        Assert.NotNull(use);
        Assert.Equal(bolt.Id, use.Ability);
        Assert.Equal(T(1, 0), use.Target.Tile);
    }

    [Fact]
    public void Story_allows_a_lightning_immune_ally_in_the_water_and_ignores_downed_allies()
    {
        var battle = On("0. 0~ 0~ 0~ 0~ 0~").With(Lightning, Force)
            .Party("mage", Caster(Lightning, Force), T(0, 0))
            .Party("immune", Tough with { Immune = Ordinal.Set("lightning") }, T(1, 0))
            .Party("down", Tough, T(2, 0)).Enemy("one", Tough, T(3, 0)).Enemy("two", Tough, T(4, 0));
        var state = battle.Start().State.Change("down", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 }) with { Difficulty = Difficulty.Story };
        Assert.Equal(Lightning.Id, Pick(battle, state)!.Ability);
    }

    [Fact]
    public void Ice_insulation_removes_the_value_of_foes_beyond_it()
    {
        var battle = On("0. 0~ 0~ 0~ 0~").With(Lightning, Force)
            .Party("mage", Caster(Lightning, Force), T(0, 0)).Enemy("one", Tough, T(1, 0)).Enemy("two", Tough, T(4, 0));
        var state = battle.Start().State with
        {
            Zones = [new Zone(0, U("mage"), "ice_sheet", [T(2, 0)], 3, false, true, [], Surface: true, Source: "freeze")], NextSerial = 1,
        };
        Assert.Equal(Force.Id, Pick(battle, state)!.Ability);
    }

    [Fact]
    public void Planning_environmental_reactions_draws_no_rng_or_mutates_state()
    {
        var battle = On("0. 0~ 0~ 0~").With(Lightning, Force)
            .Party("mage", Caster(Lightning, Force), T(0, 0)).Enemy("one", Tough, T(2, 0)).Enemy("two", Tough, T(3, 0));
        var state = battle.Start().State;
        var before = JsonSerializer.Serialize(state, ArcJson.Options);
        var first = Ai.Plan(state, U("mage"), battle.Rules);
        var second = Ai.Plan(state, U("mage"), battle.Rules);
        Assert.Equal(before, JsonSerializer.Serialize(state, ArcJson.Options));
        Assert.Equal(JsonSerializer.Serialize(first, ArcJson.Options), JsonSerializer.Serialize(second, ArcJson.Options));
    }
    private static BattleMap FuelMap()
    {
        var bytes = new byte[5 * 3]; bytes[3 * 3 + 1] = 1;
        return BattleMap.FromBytes("fuel", 5, 1, bytes, ["grass/lush", "planks/oak"], [], Ordinal.Map<ImmutableArray<Tile>>());
    }

    [Theory]
    [InlineData("material")] [InlineData("grease")] [InlineData("oil")]
    public void Fire_values_native_fuel_grease_and_oil_under_a_foe(string fuel)
    {
        var fire = Bolt("test.fire", "fire", "5");
        var battle = (fuel == "material" ? TestBattle.On(FuelMap()) : TestBattle.On("0. 0. 0. 0. 0."))
            .WithEnvironment(BasePackTests.Rules.Environment).WithAiProfiles(BasePackTests.Rules.AiProfiles.Values)
            .With(fire, Force).Party("mage", Caster(fire, Force), T(0, 0)).Enemy("foe", Tough, T(3, 0));
        var state = battle.Start().State;
        if (fuel != "material")
            state = state with { Zones = [new Zone(0, U("mage"), fuel, [T(3, 0)], 10, false, false, [], Surface: fuel == "oil", Source: "oil")], NextSerial = 1 };
        Assert.Equal(fire.Id, Pick(battle, state)!.Ability);
    }

    [Fact]
    public void Scorched_ground_does_not_add_fire_value()
    {
        var fire = Bolt("test.fire", "fire", "5");
        var battle = TestBattle.On(FuelMap()).WithEnvironment(BasePackTests.Rules.Environment).WithAiProfiles(BasePackTests.Rules.AiProfiles.Values)
            .With(fire, Force).Party("mage", Caster(fire, Force), T(0, 0)).Enemy("foe", Tough, T(3, 0));
        var state = battle.Start().State with { Zones = [new Zone(0, U("mage"), "scorched", [T(3, 0)], null, false, false, [], Surface: true)], NextSerial = 1 };
        Assert.Equal(Force.Id, Pick(battle, state)!.Ability);
    }

    [Theory]
    [InlineData(-30, true)] [InlineData(100, false)]
    public void Ice_values_the_foes_actual_slip_odds(int dex, bool freeze)
    {
        var cold = Bolt("test.cold", "cold", "5"); var force = Bolt("test.force", "force", "1d2+4");
        var battle = On("0. 0~ 0~ 0~").With(cold, force).Party("mage", Caster(cold, force), T(0, 0))
            .Enemy("foe", Tough with { Scores = Tough.Scores.SetItem(Stat.Dex, dex) }, T(2, 0));
        Assert.Equal(freeze ? cold.Id : force.Id, Pick(battle, battle.Start().State)!.Ability);
    }

    [Theory]
    [InlineData(Difficulty.Story)] [InlineData(Difficulty.Normal)]
    public void Story_rejects_ignition_that_exposes_an_adjacent_ally(Difficulty difficulty)
    {
        var fire = Bolt("test.fire", "fire", "5"); var force = Bolt("test.force", "force", "1");
        var battle = TestBattle.On(FuelMap()).WithEnvironment(BasePackTests.Rules.Environment).WithAiProfiles(BasePackTests.Rules.AiProfiles.Values)
            .With(fire, force).Party("mage", Caster(fire, force), T(0, 0)).Party("friend", Tough, T(4, 0)).Enemy("foe", Tough, T(3, 0));
        var state = battle.Start().State with { Difficulty = difficulty };
        Assert.Equal(difficulty == Difficulty.Story ? force.Id : fire.Id, Pick(battle, state)!.Ability);
    }

    [Fact]
    public void Story_rejects_even_a_possible_random_lightning_branch_near_an_ally()
    {
        var random = Lightning with { Effects = [new RandomEffect([new RandomEntry(1, Lightning.Effects),
            new RandomEntry(1, [new DamageEffect(TestAbilities.Flat("8"), "cold", [])])])] };
        var force = Bolt("test.force", "force", "1");
        var battle = On("0. 0~ 0~ 0~").With(random, force).Party("mage", Caster(random, force), T(0, 0))
            .Party("friend", Tough, T(1, 0)).Enemy("foe", Tough, T(2, 0));
        Assert.Equal(force.Id, Pick(battle, battle.Start().State with { Difficulty = Difficulty.Story })!.Ability);
    }

    [Fact]
    public void Story_rechecks_caster_exposure_after_movement_for_the_same_area_aim()
    {
        var bolt = Lightning with { Target = Lightning.Target with { Area = new AreaDef(AreaShape.Burst, 1) } };
        var force = Bolt("test.force", "force", "1");
        var battle = On("0. 0. 0. 0. 0.\n0~ 0~ 0~ 0~ 0~").With(bolt, force)
            .Party("mage", Caster(bolt, force) with { Move = 4 }, T(0, 0)).Enemy("one", Tough, T(3, 1)).Enemy("two", Tough, T(4, 1));
        var state = battle.Start().State with { Difficulty = Difficulty.Story };
        var plan = Ai.Plan(state, U("mage"), battle.Rules, new AiOptions(ProfileOverride: "caster"));
        Assert.Contains(plan.Commands, c => c is Use use && use.Ability == bolt.Id);
        foreach (var command in plan.Commands)
        {
            if (command is EndTurn) break;
            state = battle.Apply(state, command).State;
            while (state.Pending is ReactionOffer offer)
                state = battle.Apply(state, new React(offer.Reactor, offer.Options.Single(o => o.Id == Reactions.Pass))).State;
        }
        Assert.Equal(100, state.Units[U("mage")].Hp);
    }

    [Fact]
    public void Story_rejects_fire_that_would_spread_through_fuel_to_a_distant_ally()
    {
        var bytes = new byte[5 * 3];
        foreach (var x in new[] { 2, 3, 4 }) bytes[x * 3 + 1] = 1;
        var map = BattleMap.FromBytes("spread", 5, 1, bytes, ["grass/lush", "planks/oak"], [], Ordinal.Map<ImmutableArray<Tile>>());
        var fire = Bolt("test.fire", "fire", "5", range: 2); var force = Bolt("test.force", "force", "1");
        var battle = TestBattle.On(map).WithEnvironment(BasePackTests.Rules.Environment).WithAiProfiles(BasePackTests.Rules.AiProfiles.Values)
            .With(fire, force).Party("mage", Caster(fire, force), T(0, 0)).Party("friend", Tough, T(4, 0)).Enemy("foe", Tough, T(2, 0));
        Assert.Equal(force.Id, Pick(battle, battle.Start().State with { Difficulty = Difficulty.Story })!.Ability);
    }

    [Fact]
    public void A_zero_weight_lightning_branch_does_not_forbid_a_safe_spell_on_Story()
    {
        var spell = Lightning with { Effects = [new RandomEffect([new RandomEntry(0, Lightning.Effects),
            new RandomEntry(1, [new DamageEffect(TestAbilities.Flat("8"), "cold", [])])])] };
        var force = Bolt("test.force", "force", "1");
        var battle = On("0. 0~ 0~ 0~").With(spell, force).Party("mage", Caster(spell, force), T(0, 0))
            .Party("friend", Tough, T(1, 0)).Enemy("foe", Tough, T(2, 0));
        Assert.Equal(spell.Id, Pick(battle, battle.Start().State with { Difficulty = Difficulty.Story })!.Ability);
    }

}
