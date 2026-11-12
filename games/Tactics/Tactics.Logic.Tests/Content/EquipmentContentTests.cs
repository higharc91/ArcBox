namespace Tactics.Logic.Tests;

public sealed class EquipmentContentTests
{
    [Fact]
    public void An_enchanted_weapon_applies_one_attack_and_one_damage_and_gear_is_not_consumable()
    {
        var rules = BasePackTests.Rules;
        var record = SheetBuilder.DefaultCharacter("Kara", "human", "fighter", null, 3, rules);
        var plain = SheetBuilder.ForCharacter(record with { Weapon = "longsword" }, rules);
        var magic = SheetBuilder.ForCharacter(record with { Weapon = "longsword_plus_one" }, rules);
        var gear = new ItemId("gear_longsword_plus_one");
        var fixture = TestBattle.On("0. 0. 0.\n0. 0. 0.\n0. 0. 0.")
            .With([.. rules.Abilities.Values]).WithPassives([.. rules.Passives.Values]).WithConditions([.. rules.Conditions.Values])
            .WithItems(rules.Items[gear]).Party("plain", plain, new Tile(0, 1))
            .Party("magic", magic with { InitiativeBonus = 100 }, new Tile(1, 1), bag: [new(gear, 1)])
            .Enemy("enemy", TestSheets.Goblin, new Tile(2, 1));
        var state = fixture.Start().State;
        var first = Modifiers.For(state, new UnitId("plain"), fixture.Rules);
        var second = Modifiers.For(state, new UnitId("magic"), fixture.Rules);
        Assert.Equal(first.Attack + 1, second.Attack); Assert.Equal(first.Damage + 1, second.Damage);
        var result = Battle.Apply(state, new Use(new UnitId("magic"), Battle.UseItem, new Target(Unit: new UnitId("magic")), Item: gear), fixture.Rules);
        Assert.Equal(RejectCode.NoItem, result.Rejected!.Code);
        Assert.Equal(state.Units[new("magic")].Bag, result.State.Units[new("magic")].Bag);
        Assert.DoesNotContain(Choices.For(state, new("magic"), fixture.Rules).Abilities, a => a.Item == gear);
    }
}
