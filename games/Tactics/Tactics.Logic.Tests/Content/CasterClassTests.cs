using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>The shipped bard, druid and warlock kits (Task 18): inspiration, wild shape, pact regeneration, Eldritch Blast and Hex.</summary>
public sealed class CasterClassTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static AbilityId A(string id) => new(id);

    private static ConditionId C(string id) => new(id);

    private static UnitSheet Character(string cls, int level)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, level, Rules), Rules);

    private static (BattleState State, RulesPack Rules) Start(params (string Id, UnitSheet Sheet, Tile At)[] units)
    {
        const string field = """
            0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0. 0.
            """;
        var base_ = Rules;
        var map = AsciiMap.Parse(field);
        var rules = RulesPack.InMemory("caster", base_.Abilities.Values, base_.Passives.Values, base_.Conditions.Values, [map],
            base_.Injuries.Values, items: base_.Items.Values,
            monsters: base_.Monsters.Keys.Select(m => (m, SheetBuilder.ForMonster(m, base_))));
        var placements = units.Select(u => new UnitPlacement(U(u.Id), u.Sheet, new PlayerControlled(new ProfileId("pr_" + u.Id)), u.At, []));
        var setup = new BattleSetup(map, [.. placements], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        return (Battle.Start(setup, rules, seed: 1).State, rules);
    }

    [Fact]
    public void Bard_bardic_inspiration_and_countercharm()
    {
        var bard = Character("bard", 6) with { InitiativeBonus = 40 };
        var kara = Character("fighter", 6) with { InitiativeBonus = -10 };
        var (state, rules) = Start(("bard", bard, T(1, 1)), ("kara", kara, T(2, 1)),
            ("gob", SheetBuilder.ForMonster("hobgoblin", Rules), T(7, 1)));

        // Bardic Inspiration spends 1 Inspiration, grants temporary HP (1d8 + CHA at level 6) and applies Inspired.
        var res = Battle.Apply(state, new Use(U("bard"), A("bard.bardic_inspiration"), new Target(Unit: U("kara"))), rules);
        Assert.Null(res.Rejected);
        Assert.Equal(3, res.State.Units[U("bard")].Resources["inspiration"]);
        Assert.True(res.State.Units[U("kara")].TempHp >= 4);
        Assert.Contains(res.State.Units[U("kara")].Conditions, c => c.Id == C("inspired"));

        // Countercharm cleanses fear/charm and applies the Countercharm aura condition.
        var frightened = state.Give("kara", TestConditions.Lasting(rules.Conditions[C("frightened")], U("gob")));
        var counter = Battle.Apply(frightened, new Use(U("bard"), A("bard.countercharm"), new Target(Unit: U("bard"))), rules);
        Assert.Null(counter.Rejected);
        Assert.DoesNotContain(counter.State.Units[U("kara")].Conditions, c => c.Id == C("frightened"));
        Assert.Contains(counter.State.Units[U("kara")].Conditions, c => c.Id == C("countercharm"));
    }

    [Theory]
    [InlineData(2, "druid.wild_shape_wolf", "Wolf", 14, "beasts.wolf.wildshape")]
    [InlineData(4, "druid.wild_shape_bear", "Bear", 26, "beasts.bear.wildshape")]
    [InlineData(6, "druid.wild_shape_dire_wolf", "Dire Wolf", 32, "beasts.wolf.wildshape")]
    public void Druid_wild_shape_transforms_into_wolf_bear_and_dire_wolf(int level, string ability, string beastName, int beastTempHp,
        string sprite)
    {
        var druid = Character("druid", level) with { InitiativeBonus = 40 };
        var (state, rules) = Start(("druid", druid, T(1, 1)),
            ("gob", SheetBuilder.ForMonster("hobgoblin", Rules) with { InitiativeBonus = -20 }, T(5, 1)));

        var res = Battle.Apply(state, new Use(U("druid"), A(ability), new Target()), rules);
        Assert.Null(res.Rejected);
        var unit = res.State.Units[U("druid")];
        Assert.Equal(beastName, unit.Sheet.Name);
        Assert.Equal(beastTempHp, unit.TempHp);
        Assert.Equal(sprite, unit.Sheet.Sprite);
        Assert.NotNull(unit.TrueSheet);
    }

    [Fact]
    public void Warlock_regains_pact_on_even_rounds_and_scales_eldritch_blast_with_hex()
    {
        foreach (var (level, beams) in new[] { (1, 1), (5, 2), (10, 3) })
        {
            var warlock = Character("warlock", level) with
            {
                InitiativeBonus = 40,
                Scores = TestSheets.Scores(8, 14, 14, 10, 12, 20),
            };
            var dummy = SheetBuilder.ForMonster("hobgoblin", Rules) with { MaxHp = 200, Ac = 2, InitiativeBonus = -20 };
            var (state, rules) = Start(("warlock", warlock, T(1, 1)), ("gob", dummy, T(4, 1)));

            // Hex marks the target as a bonus action (spending 1 Pact), and Eldritch Blast fires 1/2/3 beams that each trigger Hex.
            var hexed = Battle.Apply(state, new Use(U("warlock"), A("warlock.hex"), new Target(Unit: U("gob"))), rules);
            Assert.Null(hexed.Rejected);
            var blast = Battle.Apply(hexed.State, new Use(U("warlock"), A("warlock.eldritch_blast"), new Target(Unit: U("gob"))), rules);
            Assert.Null(blast.Rejected);
            var rolls = blast.Events.OfType<AttackResolved>().ToArray();
            Assert.Equal(beams, rolls.Length);
            Assert.True(blast.Events.OfType<Damaged>().Count(d => d.Type == "necrotic") >= beams);

            // End round 1: at the start of round 2 (even), warlock.pact_magic restores 1 Pact.
            var endWarlock = Battle.Apply(blast.State, new EndTurn(U("warlock")), rules).State;
            var round2 = Battle.Apply(endWarlock, new EndTurn(U("gob")), rules);
            Assert.Equal(2, round2.State.Round);
            Assert.Equal(warlock.Resources["pact"], round2.State.Units[U("warlock")].Resources["pact"]);
        }
    }

    [Fact]
    public void Sorcerer_innate_sorcery_font_of_magic_and_metamagic()
    {
        var sorc = Character("sorcerer", 5) with { InitiativeBonus = 40 };
        var dummy1 = SheetBuilder.ForMonster("hobgoblin", Rules) with { MaxHp = 200, Ac = 2, InitiativeBonus = -20 };
        var dummy2 = SheetBuilder.ForMonster("hobgoblin", Rules) with { MaxHp = 200, Ac = 2, InitiativeBonus = -20 };
        var (state, rules) = Start(("sorc", sorc, T(1, 1)), ("gob1", dummy1, T(4, 1)), ("gob2", dummy2, T(4, 2)));

        // Innate Sorcery adds +1 spell DC and advantage on attacks for 3 rounds.
        var innate = Battle.Apply(state, new Use(U("sorc"), A("sorcerer.innate_sorcery"), new Target()), rules);
        Assert.Null(innate.Rejected);
        var mods = Modifiers.For(innate.State, U("sorc"), rules);
        Assert.Equal(1, mods.SpellDc);
        Assert.Equal(Edge.Advantage, mods.OwnAttacks);

        // Font of Magic converts 1 Mana -> 2 Sorcery or 2 Sorcery -> 1 Mana.
        var spentMana = state.Change("sorc", u => u with { Resources = u.Resources.SetItem("mana", 2).SetItem("sorcery", 3) });
        var toMana = Battle.Apply(spentMana, new Use(U("sorc"), A("sorcerer.font_of_magic_mana"), new Target()), rules);
        Assert.Null(toMana.Rejected);
        Assert.Equal((3, 1), (toMana.State.Units[U("sorc")].Resources["mana"], toMana.State.Units[U("sorc")].Resources["sorcery"]));

        // Twinned Spell + Chromatic Orb hits both goblins.
        var twinned = Battle.Apply(state, new Use(U("sorc"), A("sorcerer.metamagic_twinned"), new Target()), rules);
        Assert.Null(twinned.Rejected);
        var orb = Battle.Apply(twinned.State, new Use(U("sorc"), A("sorcerer.chromatic_orb"), new Target(Unit: U("gob1"))), rules);
        Assert.Null(orb.Rejected);
        Assert.Equal(2, orb.Events.OfType<AttackResolved>().Count());
    }
}
