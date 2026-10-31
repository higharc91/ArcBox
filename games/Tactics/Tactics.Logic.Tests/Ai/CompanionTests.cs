using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// Absent players' characters (spec §5.5): the <c>companion</c> profile, weighted by the class's <c>aiTags</c>, revives
/// first, spends class resources freely and uses at most one healing item from its own bag per battle, only when the threat
/// map says it would be Downed next round.
/// </summary>
public sealed class CompanionTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static AbilityDef A(string id) => Base.Abilities[new AbilityId(id)];

    private static ItemDef I(string id) => Base.Items[new ItemId(id)];

    private static Tile T(int x, int y) => new(x, y);

    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 30 };

    private static readonly AiControlled Companion = new("companion");

    /// <summary>The adapter lets absent players' characters use their bag (the companion rules still gate it).</summary>
    private static readonly AiOptions Absent = new(AllowItems: true);

    /// <summary>A builder on <paramref name="ascii"/> with the base pack's AI profiles and its healing and fire items.</summary>
    private static TestBattle On(string ascii, params AiProfileDef[] extra) => TestBattle.On(ascii)
        .WithAiProfiles([.. Base.AiProfiles.Values, .. extra])
        .With(A("common.use_item"), A("item.potion_of_healing"), A("item.alchemists_fire"))
        .WithConditions(Base.Conditions[new ConditionId("burning")])
        .WithItems(I("potion_of_healing"), I("alchemists_fire"));

    private static Use? UseOf(TurnPlan plan, string ability)
        => plan.Commands.OfType<Use>().FirstOrDefault(u => u.Ability.Value == ability);

    private static IEnumerable<Use> ItemUses(TurnPlan plan) => plan.Commands.OfType<Use>().Where(u => u.Item is not null);

    /// <summary>
    /// Applies the plan's commands (every one accepted) until the battle ends, stopping before its <see cref="EndTurn"/> when
    /// asked.
    /// </summary>
    private static BattleState ApplyAll(BattleState state, TurnPlan plan, RulesPack rules, bool endTurn = true)
    {
        foreach (var command in plan.Commands.Where(c => endTurn || c is not EndTurn).TakeWhile(_ => state.Outcome is null))
        {
            var resolution = Battle.Apply(state, command, rules);
            Assert.True(resolution.Rejected is null, $"{command} was refused: {resolution.Rejected?.Reason}\n{plan.Explain}");
            state = resolution.State;
        }
        return state;
    }

    [Fact]
    public void Revives_downed_ally_first()
    {
        // Vex lies Downed between the fighter and a goblin it could hit instead.
        TestBattle Battle(string profile) => On("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """)
            .Party("kara", First(TestSheets.Fighter), T(2, 1), new AiControlled(profile))
            .Party("vex", TestSheets.Rogue, T(1, 1))
            .Enemy("gob", TestSheets.Goblin, T(3, 1));
        BattleState Downed(TestBattle battle)
            => battle.Start().State.Change("vex", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 2 });

        var battle = Battle("companion");
        var state = Downed(battle);
        var plan = Ai.Plan(state, U("kara"), battle.Rules, Absent);

        Assert.True(U("vex") == UseOf(plan, "common.help")?.Target.Unit, plan.Explain);
        Assert.Null(UseOf(plan, "common.attack"));
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(LifeState.Up, after.Units[U("vex")].Life);

        // A brute, which weighs reviving low, takes the swing at the goblin instead.
        var brute = Battle("brute");
        Assert.Equal(U("gob"), UseOf(Ai.Plan(Downed(brute), U("kara"), brute.Rules), "common.attack")?.Target.Unit);

        // Revive is the companion's highest weight, whatever the class.
        var profile = AiProfile.For(state, U("kara"), battle.Rules);
        Assert.True(new[] { profile.Damage, profile.Kill, profile.Control, profile.Heal, profile.Caution, profile.FocusFire, profile.Objective }
            .All(w => w < profile.Revive), profile.ToString());
    }

    /// <summary>
    /// Kara (AC 12, 20 max HP, two potions) stands among three goblins, which can be expected to deal about 11 damage to her
    /// next round. She acts first.
    /// </summary>
    private static (TestBattle Battle, BattleState State) Surrounded(int hp)
    {
        var battle = On("""
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """)
            .Party("kara", First(TestSheets.Fighter) with { Ac = 12, MaxHp = 20 }, T(2, 1), Companion,
                [new ItemStack(new ItemId("potion_of_healing"), 2)])
            .Enemy("g1", TestSheets.Goblin, T(1, 1))
            .Enemy("g2", TestSheets.Goblin, T(3, 1))
            .Enemy("g3", TestSheets.Goblin, T(2, 0));
        return (battle, battle.Start().State.Change("kara", u => u with { Hp = hp }));
    }

    [Fact]
    public void Uses_one_healing_potion_only_when_threat_would_down_it()
    {
        // At 4 HP the goblins' threat (about 11) would drop her: she drinks one potion (her bonus action), on herself.
        var (battle, state) = Surrounded(hp: 4);
        var danger = ThreatMap.For(state, Side.Party, battle.Rules).At(T(2, 1));
        Assert.InRange(danger, 4, 14);
        var plan = Ai.Plan(state, U("kara"), battle.Rules, Absent);

        var drink = Assert.Single(ItemUses(plan));
        Assert.Equal((Battle.UseItem, new ItemId("potion_of_healing"), U("kara")), (drink.Ability, drink.Item!.Value, drink.Target.Unit!.Value));
        var after = ApplyAll(state, plan, battle.Rules);
        Assert.Equal(1, after.Units[U("kara")].ItemsUsedByAi);
        Assert.Equal([new ItemUse(U("kara"), new ItemId("potion_of_healing"), ByAi: true)], after.ItemsUsed);
        Assert.Equal([new ItemStack(new ItemId("potion_of_healing"), 1)], after.Units[U("kara")].Bag);

        // At 14 HP the same threat wouldn't drop her: no potion, though she is hurt and her bonus action is free.
        var (_, healthier) = Surrounded(hp: 14);
        var calm = Ai.Plan(healthier, U("kara"), battle.Rules, Absent);
        Assert.Empty(ItemUses(calm));
    }

    [Fact]
    public void Never_uses_second_item_in_same_battle()
    {
        var (battle, state) = Surrounded(hp: 4);
        var first = Ai.Plan(state, U("kara"), battle.Rules, Absent);
        Assert.Single(ItemUses(first));

        // Still her turn, a potion left and in worse danger than before, with everything to spend again: no second item.
        var drank = ApplyAll(state, first, battle.Rules, endTurn: false);
        Assert.Equal(1, drank.Units[U("kara")].ItemsUsedByAi);
        var again = drank.Change("kara", u => u with { Hp = 2, Economy = u.Economy with { Action = true, Bonus = true, MoveLeft = 4 } });

        var second = Ai.Plan(again, U("kara"), battle.Rules, Absent);

        Assert.True(!ItemUses(second).Any(), second.Explain);
        Assert.Contains(again.Units[U("kara")].Bag, s => s.Item == new ItemId("potion_of_healing") && s.Count == 1);
    }

    [Fact]
    public void Never_uses_non_healing_item()
    {
        // Kara, in danger and with only a stick to hit with, has alchemist's fire in her bag. With the companion's own weights
        // under another name (no companion rules), the AI throws it; a companion leaves it there.
        var unruled = Base.AiProfiles["companion"] with { Id = "unruled" };
        (TestBattle, BattleState) Battle(string profile)
        {
            var battle = On("""
                0. 0. 0. 0. 0. 0.
                0. 0. 0. 0. 0. 0.
                0. 0. 0. 0. 0. 0.
                """, unruled)
                .Party("kara", First(TestSheets.Fighter) with
                {
                    Ac = 12, MaxHp = 20, Weapon = TestSheets.Fighter.Weapon with { Damage = Dice.Parse("1d2") },
                }, T(0, 1), new AiControlled(profile), [new ItemStack(new ItemId("alchemists_fire"), 2)])
                .Enemy("g1", TestSheets.Goblin, T(4, 0))
                .Enemy("g2", TestSheets.Goblin, T(4, 1))
                .Enemy("g3", TestSheets.Goblin, T(4, 2));
            return (battle, battle.Start().State.Change("kara", u => u with { Hp = 4 }));
        }

        var (other, otherState) = Battle("unruled");
        var thrown = Ai.Plan(otherState, U("kara"), other.Rules, Absent);
        Assert.True(ItemUses(thrown).Any(u => u.Item == new ItemId("alchemists_fire")), thrown.Explain);

        var (battle, state) = Battle("companion");
        Assert.True(ThreatMap.For(state, Side.Party, battle.Rules).At(T(0, 1)) >= 4);
        var plan = Ai.Plan(state, U("kara"), battle.Rules, Absent);
        Assert.True(!ItemUses(plan).Any(), plan.Explain);
        ApplyAll(state, plan, battle.Rules);
    }

    [Fact]
    public void Spends_class_resources_freely()
    {
        // A level-1 mage with a single Mana against one goblin: Magic Missile (1 Mana, sure to hit) or Fire Bolt (free). A
        // profile that hoards keeps the Mana; the companion, whose resources refill every battle, spends it.
        var missile = A("wizard.magic_missile");
        var bolt = A("wizard.fire_bolt");
        var mage = First(TestSheets.Cleric) with
        {
            CastingStat = "Int", Scores = TestSheets.Scores(8, 14, 12, 16, 10, 10), Resources = Ordinal.Map(("mana", 1)),
            Abilities = [missile.Id, bolt.Id],
        };
        var miser = Base.AiProfiles["companion"] with { Id = "miser", Hoard = 3 };
        (TestBattle, BattleState) Battle(string profile)
        {
            var battle = On("""
                0. 0. 0. 0. 0. 0.
                0. 0. 0. 0. 0. 0.
                """, miser).With(missile, bolt)
                .Party("mage", mage, T(0, 0), new AiControlled(profile))
                .Enemy("gob", TestSheets.Goblin, T(5, 1));
            return (battle, battle.Start().State);
        }

        var (hoarding, hoardState) = Battle("miser");
        var kept = Ai.Plan(hoardState, U("mage"), hoarding.Rules, Absent);
        Assert.True(UseOf(kept, "wizard.fire_bolt") is not null, kept.Explain);

        var (companion, state) = Battle("companion");
        var plan = Ai.Plan(state, U("mage"), companion.Rules, Absent);
        Assert.True(UseOf(plan, "wizard.magic_missile") is not null, plan.Explain);
        Assert.Equal(0, ApplyAll(state, plan, companion.Rules).Units[U("mage")].Resources["mana"]);
        Assert.Equal(0, AiProfile.For(state, U("mage"), companion.Rules).Hoard);
    }

    [Fact]
    public void Role_weights_come_from_class_aiTags()
    {
        // A cleric (healer, support, caster) heals and controls more; a fighter (defender, melee, front) stands in harm's way.
        UnitSheet Character(string cls) => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, 3, Base), Base);
        var state = On("0. 0. 0.")
            .Party("cleric", Character("cleric"), T(0, 0), Companion)
            .Party("fighter", Character("fighter"), T(1, 0), Companion)
            .Enemy("gob", TestSheets.Goblin, T(2, 0))
            .Start().State;
        var plain = AiProfile.From(Base.AiProfiles["companion"]);

        var cleric = AiProfile.For(state, U("cleric"), Base);
        var fighter = AiProfile.For(state, U("fighter"), Base);

        Assert.Equal("companion", cleric.Id);
        Assert.True(cleric.Heal > plain.Heal && cleric.Control > plain.Control, cleric.ToString());
        Assert.True(fighter.Caution < plain.Caution && fighter.Objective > plain.Objective, fighter.ToString());
        Assert.Equal(plain.Heal, fighter.Heal);
        Assert.All(new[] { cleric, fighter }, p => Assert.True(p.Revive > p.Heal && p.Revive > p.Damage && p.Revive > p.Objective));
    }

    [Fact]
    public void Guards_the_healer()
    {
        // Kara can't reach the goblins this turn, but they can reach the cleric next round. A companion fighter steps in
        // beside her; a brute walks on towards the goblins.
        UnitSheet Character(string cls) => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, 3, Base), Base);
        TestBattle Battle(string profile) => On("""
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0. 0.
            """)
            .Party("kara", First(Character("fighter")), T(1, 2), new AiControlled(profile))
            .Party("priest", Character("cleric"), T(2, 0))
            .Enemy("g1", TestSheets.Goblin, T(7, 0))
            .Enemy("g2", TestSheets.Goblin, T(7, 1));
        Tile End(BattleState state, TurnPlan plan) => plan.Commands.OfType<Move>().LastOrDefault()?.Path[^1] ?? state.Units[U("kara")].Pos;

        var guard = Battle("companion");
        var state = guard.Start().State;
        var plan = Ai.Plan(state, U("kara"), guard.Rules, Absent);
        Assert.True(Reach.Distance(End(state, plan), T(2, 0)) <= 1, plan.Explain);

        var brute = Battle("brute");
        var bruteState = brute.Start().State;
        var charge = Ai.Plan(bruteState, U("kara"), brute.Rules);
        Assert.True(Reach.Distance(End(bruteState, charge), T(2, 0)) > 1, charge.Explain);
    }

    [Fact]
    public void Hound_spawns_at_battle_start_and_acts_after_owner()
    {
        var rec = SheetBuilder.DefaultCharacter("Vex", "elf", "ranger", "ranger.beastmaster", 5, Base);
        var sheet = SheetBuilder.ForCharacter(rec, Base);
        var map = AsciiMap.Parse("0. 0. 0. 0. 0.\n0. 0. 0. 0. 0.");
        var rules = RulesPack.InMemory("hound_test", Base.Abilities.Values, Base.Passives.Values, Base.Conditions.Values,
            [map], Base.Injuries.Values, monsters: [("hound", Base.MonsterSheet("hound", 5)!)]);
        var setup = new BattleSetup(map,
            [
                new UnitPlacement(U("ranger"), sheet, new PlayerControlled(new ProfileId("pr_ranger")), T(0, 0), []),
                new UnitPlacement(U("goblin"), TestSheets.Goblin, new AiControlled("brute"), T(4, 1), []),
            ],
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);

        var b = Battle.Start(setup, rules, 1).State;

        Assert.Contains(U("ranger.hound.0"), b.Units.Keys);
        var idx = b.Order.IndexOf(U("ranger"));
        Assert.Equal(U("ranger.hound.0"), b.Order[idx + 1]);
        Assert.Equal(26, Base.MonsterSheet("hound", 6)!.MaxHp);
        Assert.Equal(38, Base.MonsterSheet("hound", 10)!.MaxHp);
    }
}
