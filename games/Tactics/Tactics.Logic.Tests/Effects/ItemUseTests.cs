using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary><c>common.use_item</c> with a <see cref="Use.Item"/>: the item's ability runs and one item leaves the bag.</summary>
public sealed class ItemUseTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static readonly ItemId Potion = new("potion");

    private static readonly AbilityDef UseItem = new(new AbilityId("common.use_item"), "Use an Item", 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Self, 0, false, new AreaDef(AreaShape.Self)), []);

    private static readonly AbilityDef Drink = new(new AbilityId("item.potion"), "Potion", 0, new CostDef(Bonus: 1),
        new TargetDef(TargetKind.Ally, 1, false, new AreaDef(AreaShape.Single, IncludeSelf: true)),
        [new HealEffect(TestAbilities.Flat("4"), [])]);

    /// <summary>A hurt cleric (3 of 10 HP) with two potions acts first; a goblin stands far away.</summary>
    private static (TestBattle Battle, BattleState State) Start(int potions = 2)
    {
        var battle = TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with { InitiativeBonus = 10 }, new Tile(0, 0), bag: [new ItemStack(Potion, potions)])
            .Enemy("gob", TestSheets.Goblin, new Tile(5, 1))
            .With(UseItem, Drink)
            .WithItems(new ItemDef(Potion, "Potion of healing", Drink.Id, Ordinal.Set("healing")));
        var (state, _) = battle.Start();
        return (battle, state.Change("cleric", u => u with { Hp = 3 }));
    }

    private static Use Using(ItemId? item, AbilityId? ability = null, string target = "cleric")
        => new(U("cleric"), ability ?? UseItem.Id, new Target(Unit: U(target)), item);

    [Fact]
    public void A_potion_runs_its_ability_and_is_consumed()
    {
        var (battle, state) = Start();

        var (after, events) = battle.Apply(state, Using(Potion));

        Assert.Equal<BattleEvent>(
            [new AbilityUsed(U("cleric"), Drink.Id, new Target(Unit: U("cleric")), null), new Healed(U("cleric"), 4, 7)], events);
        var cleric = after.Units[U("cleric")];
        Assert.Equal([new ItemStack(Potion, 1)], cleric.Bag);
        Assert.Equal((true, false), (cleric.Economy.Action, cleric.Economy.Bonus));   // the potion's own cost: a bonus action
        Assert.Equal([new ItemUse(U("cleric"), Potion, ByAi: false)], after.ItemsUsed);

        // The last one leaves the bag empty, and the battle's result lists every use.
        var (oneLeft, last) = Start(potions: 1);
        (last, _) = oneLeft.Apply(last, Using(Potion));
        Assert.Empty(last.Units[U("cleric")].Bag);
        var result = Battle.Result(last with { Outcome = BattleOutcome.Won }, battle.Rules)!;
        Assert.Equal([new ItemUse(U("cleric"), Potion, false)], result.ItemsUsed[U("cleric")]);
    }

    [Fact]
    public void Items_not_in_the_bag_or_not_through_use_item_are_refused()
    {
        var (battle, state) = Start(potions: 0);
        var noItem = new Rejection(RejectCode.NoItem, "You don't have that item.");

        Assert.Equal(noItem, Battle.Apply(state, Using(Potion), battle.Rules).Rejected);
        Assert.Equal(noItem, Battle.Apply(state, Using(new ItemId("elixir")), battle.Rules).Rejected);
        Assert.Equal(noItem, Battle.Apply(state, Using(null), battle.Rules).Rejected);
        (_, state) = Start();
        Assert.Equal(noItem, Battle.Apply(state, Using(Potion, RulesPack.CommonHelp.Id), battle.Rules).Rejected);
        // An item's ability isn't known on its own.
        Assert.Equal(RejectCode.UnknownAbility, Battle.Apply(state, Using(null, Drink.Id), battle.Rules).Rejected!.Code);
        Assert.Equal(RejectCode.InvalidTarget, Battle.Apply(state, Using(Potion, target: "gob"), battle.Rules).Rejected!.Code);
        Assert.Equal(2, state.Units[U("cleric")].Bag[0].Count);
    }
}
