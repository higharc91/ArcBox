using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class PhoneEnvironmentTests
{
    private static readonly UnitId Mage = new("mage");
    private static readonly AbilityDef Bolt = new(new AbilityId("test.phone_bolt"), "Bolt", 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Tile, 4, false, new AreaDef(AreaShape.Single, FriendlyFire: true)), [new DamageEffect(TestAbilities.Flat("8"), "lightning", [])]);
    private static TestBattle On(bool enabled = true) => TestBattle.On("0. 0~ 0~ 0~ 0~")
        .WithEnvironment(BasePackTests.Rules.Environment, enabled).With(Bolt)
        .Party("mage", (TestSheets.Cleric with { InitiativeBonus = 100 }).Knowing(Bolt), new Tile(0, 0))
        .Party("friend", TestSheets.Fighter, new Tile(2, 0)).Enemy("foe", TestSheets.Fighter, new Tile(3, 0));

    [Fact]
    public void Empty_water_aim_retains_spread_and_conducted_allies_without_mutating_state()
    {
        var b = On(); var state = b.Start().State;
        var before = JsonSerializer.Serialize(state, ArcJson.Options);
        var target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == Bolt.Id).Targets.Single(t => t.Aim == new Tile(1, 0));
        Assert.Contains(new Tile(3, 0), target.Spread);
        Assert.Contains(target.Affected, u => u.Unit == new UnitId("friend") && u.Damage > 0);
        Assert.Contains(target.Affected, u => u.Unit == new UnitId("foe") && u.Damage > 0);
        Assert.Equal(before, JsonSerializer.Serialize(state, ArcJson.Options));
    }

    [Fact]
    public void Ice_blocks_the_spread_preview_and_disabled_encounters_emit_no_optional_fields()
    {
        var b = On(); var state = b.Start().State;
        state = state with { Zones = [new Zone(1, Mage, "ice_sheet", [new Tile(2, 0)], 3, false, true, [], Surface: true)] };
        var target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == Bolt.Id).Targets.Single(t => t.Aim == new Tile(1, 0));
        Assert.DoesNotContain(new Tile(3, 0), target.Spread);
        Assert.DoesNotContain(target.Affected, u => u.Unit == new UnitId("foe"));
        var off = On(false);
        var json = JsonSerializer.Serialize(Choices.For(off.Start().State, Mage, off.Rules), ArcJson.Options);
        Assert.DoesNotContain("\"risk\"", json);
        Assert.DoesNotContain("\"warn\"", json);
        Assert.DoesNotContain("\"spread\"", json);
    }

    [Fact]
    public void Movement_warns_about_burning_path_and_next_turn_damage()
    {
        var b = TestBattle.On("0. 0. 0. 0.").WithEnvironment(BasePackTests.Rules.Environment)
            .Party("mage", TestSheets.Fighter with { InitiativeBonus = 100 }, new Tile(0, 0)).Enemy("foe", TestSheets.Fighter, new Tile(3, 0));
        var state = b.Start().State;
        var def = b.Rules.Environment.Surfaces["burning"];
        state = state with { Zones = [new Zone(1, Mage, "burning", [new Tile(1, 0)], 2, false, false, def.Passives, Surface: true)] };
        var move = Choices.For(state, Mage, b.Rules).Moves.Single(m => m.To == new Tile(1, 0));
        Assert.Equal(7, move.Risk);
        Assert.Contains("burns", move.Warn);
    }
    [Fact]
    public void Direct_hits_are_not_counted_twice_and_downed_units_are_not_conducted()
    {
        var b = On(); var state = b.Start().State;
        var foe = new UnitId("foe");
        state = state with { Units = state.Units.SetItem(foe, state.Units[foe] with { Life = LifeState.Downed, Hp = 0, Countdown = 3 }) };
        var target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == Bolt.Id).Targets.Single(t => t.Aim == new Tile(2, 0));
        Assert.Equal(8, Assert.Single(target.Affected, u => u.Unit == new UnitId("friend")).Damage);
        Assert.DoesNotContain(target.Affected, u => u.Unit == foe);
    }

    [Fact]
    public void Fire_spread_preview_respects_fuel_water_and_scorched_ground()
    {
        var fire = Bolt with { Id = new AbilityId("test.fire"), Effects = [new DamageEffect(TestAbilities.Flat("4"), "fire", [])] };
        var b = TestBattle.On("0. 0. 0~ 0.").WithEnvironment(BasePackTests.Rules.Environment).With(fire)
            .Party("mage", (TestSheets.Cleric with { InitiativeBonus = 100 }).Knowing(fire), new Tile(0, 0))
            .Enemy("foe", TestSheets.Fighter, new Tile(3, 0));
        var state = b.Start().State with { Zones = [new Zone(1, Mage, "oil", [new Tile(1, 0)], 10, false, false, [], Surface: true)] };
        var target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == fire.Id).Targets.Single(t => t.Aim == new Tile(1, 0));
        Assert.Equal([new Tile(1, 0)], target.Spread);
        state = state with { Zones = state.Zones.Add(new Zone(2, Mage, "scorched", [new Tile(1, 0)], null, false, false, [], Surface: true)) };
        target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == fire.Id).Targets.Single(t => t.Aim == new Tile(1, 0));
        Assert.True(target.Spread.IsDefaultOrEmpty);
        target = Choices.For(state, Mage, b.Rules).Abilities.Single(a => a.Ability == fire.Id).Targets.Single(t => t.Aim == new Tile(2, 0));
        Assert.True(target.Spread.IsDefaultOrEmpty);
    }

}
