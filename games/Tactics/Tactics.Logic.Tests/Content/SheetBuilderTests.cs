using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class SheetBuilderTests
{
    /// <summary>The minimal base pack plus the sheets chapter: fighter and rogue, human, wanderer, dwarf and hopper, scholar and soldier.</summary>
    internal static readonly RulesPack Rules = RulesPack.Load(
        [ContentPaths.Fixture("minimal", "base"), ContentPaths.Fixture("sheets", "chapter-01")]);

    private static ImmutableSortedDictionary<Stat, int> Scores(int str, int dex, int con, int @int, int wis, int cha)
        => TestSheets.Scores(str, dex, con, @int, wis, cha);

    private static readonly ImmutableSortedDictionary<Stat, int> NoIncreases = ImmutableSortedDictionary<Stat, int>.Empty;

    /// <summary>A rogue (a wanderer, who has no racial traits) with DEX 15, CON 14, INT 12 and WIS 10 before the scholar background (+2 INT, +1 WIS).</summary>
    private static CharacterRecord Rogue(int level = 1, string race = "wanderer", string? subclass = null) => new(
        "Vex", race, "rogue", subclass, "scholar", level, Scores(8, 15, 14, 12, 10, 8), NoIncreases, [], [], "rapier", "leather",
        Shield: false, []);

    /// <summary>A fighter with STR 15, DEX 12, CON 13 before the soldier background (+2 STR, +1 CON).</summary>
    private static CharacterRecord Fighter(int level = 1, string armor = "chain_mail", bool shield = false) => new(
        "Brom", "human", "fighter", null, "soldier", level, Scores(15, 12, 13, 8, 10, 8), NoIncreases, [], [], "longsword", armor,
        shield, []);

    private static ImmutableArray<ContentError> ErrorsOf(CharacterRecord c)
        => Assert.Throws<ContentException>(() => SheetBuilder.ForCharacter(c, Rules)).Errors;

    private static (BattleState State, UnitId Id) Started(UnitSheet sheet)
    {
        var placement = new UnitPlacement(new UnitId("hero"), sheet, new PlayerControlled(new ProfileId("pr_hero")), new Tile(0, 3), []);
        var wolf = new UnitPlacement(new UnitId("wolf"), SheetBuilder.ForMonster("wolf", Rules), new AiControlled("brute"), new Tile(5, 0), []);
        var setup = new BattleSetup(Rules.MapFor("field")!, [placement, wolf], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        return (Battle.Start(setup, Rules, 1).State, placement.Id);
    }

    // ---- Hit points and proficiency ---------------------------------------------------------------------------------

    [Fact]
    public void Level_1_hp_is_full_die_plus_con()
    {
        var rogue = SheetBuilder.ForCharacter(Rogue(), Rules);      // d8 + CON 14 (+2)
        var fighter = SheetBuilder.ForCharacter(Fighter(), Rules);  // d10 + CON 14 (13 + 1 from soldier)

        Assert.Equal(10, rogue.MaxHp);
        Assert.Equal(12, fighter.MaxHp);
    }

    [Fact]
    public void Level_5_hp_adds_average_per_level()
    {
        // Rogue: 8 + 2, then 4 levels of ceil(8/2) + 1 + 2 = 7. Fighter: 10 + 2, then 4 of 5 + 1 + 2 = 8.
        Assert.Equal(38, SheetBuilder.ForCharacter(Rogue(5), Rules).MaxHp);
        Assert.Equal(44, SheetBuilder.ForCharacter(Fighter(5), Rules).MaxHp);
        Assert.Equal(17, SheetBuilder.ForCharacter(Rogue(2), Rules).MaxHp);
    }

    [Fact]
    public void Hp_uses_con_after_background_and_increases()
    {
        // CON 13 + soldier 1 = 14 (+2); an increase of +2 makes 16 (+3), counted on every level.
        var fighter = Fighter(4) with { Increases = ImmutableSortedDictionary<Stat, int>.Empty.Add(Stat.Con, 2) };

        var sheet = SheetBuilder.ForCharacter(fighter, Rules);

        Assert.Equal(16, sheet.Score(Stat.Con));
        Assert.Equal(10 + 3 + 3 * (5 + 1 + 3), sheet.MaxHp);
    }

    [Fact]
    public void Proficiency_by_level()
    {
        var proficiency = Enumerable.Range(1, 10).Select(l => Stats.Proficiency(SheetBuilder.ForCharacter(Rogue(l), Rules).Level));

        Assert.Equal([2, 2, 2, 2, 3, 3, 3, 3, 4, 4], proficiency);
    }

    // ---- Scores, AC and initiative ----------------------------------------------------------------------------------

    [Fact]
    public void Background_boosts_and_increases_apply_to_base_scores()
    {
        var sheet = SheetBuilder.ForCharacter(Rogue(8) with
        {
            Increases = ImmutableSortedDictionary<Stat, int>.Empty.Add(Stat.Dex, 2).Add(Stat.Wis, 1).Add(Stat.Cha, 1),
        }, Rules);

        Assert.Equal(Scores(8, 17, 14, 14, 12, 9), sheet.Scores);
    }

    [Fact]
    public void Medium_armor_caps_dex_2()
    {
        // DEX 16 (+3): scale mail (14) gets +2 only, with a shield +2 more.
        var scale = Fighter(armor: "scale_mail") with { BaseScores = Scores(15, 16, 13, 8, 10, 8) };

        var plain = SheetBuilder.ForCharacter(scale, Rules);
        var shielded = SheetBuilder.ForCharacter(scale with { Shield = true }, Rules);

        Assert.Equal((16, ArmorKind.Medium), (plain.Ac, plain.Armor));
        Assert.Equal(18, shielded.Ac);
    }

    [Fact]
    public void Light_armor_has_no_dex_cap_and_heavy_armor_ignores_dex()
    {
        var dex16 = Scores(15, 16, 13, 8, 10, 8);

        var light = SheetBuilder.ForCharacter(Fighter(armor: "leather") with { BaseScores = dex16 }, Rules);
        var heavy = SheetBuilder.ForCharacter(Fighter(armor: "chain_mail") with { BaseScores = dex16 }, Rules);
        var heavyShield = SheetBuilder.ForCharacter(Fighter(armor: "chain_mail", shield: true), Rules);
        var bare = SheetBuilder.ForCharacter(Fighter(armor: "") with { BaseScores = dex16 }, Rules);
        var named = SheetBuilder.ForCharacter(Fighter(armor: "none") with { BaseScores = dex16 }, Rules);

        Assert.Equal((14, ArmorKind.Light), (light.Ac, light.Armor));
        Assert.Equal((16, ArmorKind.Heavy), (heavy.Ac, heavy.Armor));
        Assert.Equal(18, heavyShield.Ac);
        Assert.Equal((13, ArmorKind.None), (bare.Ac, bare.Armor));    // 10 + 3: no armour at all
        Assert.Equal((13, ArmorKind.None), (named.Ac, named.Armor));
    }

    [Fact]
    public void Unarmored_defense_adds_stat_modifier_only_when_unarmored()
    {
        var barb = SheetBuilder.DefaultCharacter("Korg", "human", "barbarian", null, 1, BasePackTests.Rules) with
        {
            BaseScores = Scores(15, 14, 15, 8, 10, 8),
        };
        var unarmored = SheetBuilder.ForCharacter(barb with { Armor = "", Shield = false }, BasePackTests.Rules);
        var shielded = SheetBuilder.ForCharacter(barb with { Armor = "", Shield = true }, BasePackTests.Rules);
        var armored = SheetBuilder.ForCharacter(barb with { Armor = "scale_mail", Shield = false }, BasePackTests.Rules);
        var dexMod = Stats.Mod(unarmored.Score(Stat.Dex));
        var conMod = Stats.Mod(unarmored.Score(Stat.Con));

        Assert.True(conMod > 0);
        Assert.Equal(10 + dexMod + conMod, unarmored.Ac);
        Assert.Equal(10 + dexMod + conMod + 2, shielded.Ac);
        Assert.Equal(14 + Math.Min(dexMod, 2), armored.Ac);
    }

    [Fact]
    public void Negative_dex_lowers_light_armor_but_not_heavy()
    {
        var weak = Scores(15, 8, 13, 8, 10, 8);     // DEX 8 (−1)

        Assert.Equal(10, SheetBuilder.ForCharacter(Fighter(armor: "leather") with { BaseScores = weak }, Rules).Ac);
        Assert.Equal(16, SheetBuilder.ForCharacter(Fighter(armor: "chain_mail") with { BaseScores = weak }, Rules).Ac);
    }

    [Fact]
    public void Heavy_armor_initiative_minus_2()
    {
        // DEX 12 (+1): 10 + 1 − 2 in chain mail, counted once (the sheet's bonus stays 0), and 10 + 1 in scale mail.
        var heavy = SheetBuilder.ForCharacter(Fighter(armor: "chain_mail"), Rules);
        var medium = SheetBuilder.ForCharacter(Fighter(armor: "scale_mail"), Rules);

        var (state, id) = Started(heavy);

        Assert.Equal(0, heavy.InitiativeBonus);
        Assert.Equal(9, TurnOrder.Initiative(state, id, Rules));
        Assert.Equal(11, TurnOrder.Initiative(Started(medium).State, id, Rules));
    }

    // ---- Race, class and features -----------------------------------------------------------------------------------

    [Fact]
    public void Dwarf_move_3()
    {
        var dwarf = SheetBuilder.ForCharacter(Rogue(race: "dwarf"), Rules);
        var human = SheetBuilder.ForCharacter(Rogue(), Rules);   // a wanderer: Move 4

        Assert.Equal(3, dwarf.Move);
        Assert.Equal(4, human.Move);
        Assert.Equal(["poison"], dwarf.Resist);
        Assert.Empty(human.Resist);
    }

    [Fact]
    public void Jump_is_the_classes_unless_the_race_sets_one()
    {
        Assert.Equal(3, SheetBuilder.ForCharacter(Rogue(), Rules).Jump);
        Assert.Equal(2, SheetBuilder.ForCharacter(Fighter(), Rules).Jump);
        Assert.Equal(4, SheetBuilder.ForCharacter(Rogue(race: "hopper"), Rules).Jump);
    }

    [Fact]
    public void Race_ability_and_trait_passives_come_with_the_race()
    {
        var human = SheetBuilder.ForCharacter(Fighter(), Rules);     // human: passive test.tough, ability test.mend

        Assert.Contains(new AbilityId("test.mend"), human.Abilities);
        Assert.Contains("test.tough", human.Passives);
    }

    [Fact]
    public void Features_only_up_to_level()
    {
        var one = SheetBuilder.ForCharacter(Rogue(1), Rules);
        var four = SheetBuilder.ForCharacter(Rogue(4), Rules);
        var ten = SheetBuilder.ForCharacter(Rogue(10), Rules);

        Assert.Equal([new AbilityId("rogue.hide")], one.Abilities);
        Assert.Equal(["test.sneak"], one.Passives);
        Assert.Equal(1, one.Attacks);

        // From level 2 the class's default pick, test.bolt, comes with them.
        Assert.Equal([new AbilityId("rogue.hide"), new AbilityId("rogue.step"), new AbilityId("test.bolt")], four.Abilities);
        Assert.Equal(["test.sneak", "rogue.evasion"], four.Passives);
        Assert.DoesNotContain(new AbilityId("rogue.dash"), four.Abilities);

        Assert.Equal(3, ten.Attacks);
        Assert.Equal(2, SheetBuilder.ForCharacter(Rogue(5), Rules).Attacks);
        Assert.Contains(new AbilityId("rogue.blind"), ten.Abilities);
        Assert.Equal(["test.sneak", "rogue.evasion", "test.tough", "rogue.hardy"], ten.Passives);   // rogue.evasion again at 9 is not repeated
    }

    [Fact]
    public void Subclass_features_at_3_6_10()
    {
        AbilityId[] At(int level, string? subclass) => [.. SheetBuilder.ForCharacter(Rogue(level, subclass: subclass), Rules).Abilities];
        string[] Passives(int level) => [.. SheetBuilder.ForCharacter(Rogue(level, subclass: "rogue.shadow"), Rules).Passives];

        Assert.DoesNotContain(new AbilityId("rogue.shadowstep"), At(2, "rogue.shadow"));
        Assert.Contains(new AbilityId("rogue.shadowstep"), At(3, "rogue.shadow"));
        Assert.DoesNotContain("rogue.shadow_sense", Passives(5));
        Assert.Contains("rogue.shadow_sense", Passives(6));
        Assert.DoesNotContain(new AbilityId("rogue.umbra"), At(9, "rogue.shadow"));
        Assert.Contains(new AbilityId("rogue.umbra"), At(10, "rogue.shadow"));
        Assert.DoesNotContain(new AbilityId("rogue.shadowstep"), At(10, null));    // no subclass, no subclass features
    }

    [Fact]
    public void Class_resources_by_level_and_saves()
    {
        var one = SheetBuilder.ForCharacter(Rogue(1), Rules);
        var five = SheetBuilder.ForCharacter(Rogue(5), Rules);
        var nine = SheetBuilder.ForCharacter(Rogue(9), Rules);

        Assert.Equal([("edge", 2)], one.Resources.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(3, five.Resources["edge"]);
        Assert.Equal(4, nine.Resources["edge"]);
        Assert.Equal([Stat.Dex, Stat.Int], one.SaveProficiencies);
        Assert.Same(StringComparer.Ordinal, one.Resources.KeyComparer);
    }

    [Fact]
    public void Casting_stat_and_the_rest_of_the_sheet()
    {
        var sheet = SheetBuilder.ForCharacter(Rogue(3, subclass: "rogue.shadow"), Rules);

        Assert.Equal(("Vex", Side.Party, 3), (sheet.Name, sheet.Side, sheet.Level));
        Assert.Null(sheet.CastingStat);
        Assert.Equal("rapier", sheet.Weapon.Id);
        Assert.True(sheet.Weapon.Finesse);
        Assert.Null(sheet.Weapon.Replaces);
        Assert.Equal(["acrobatics", "arcana", "history", "stealth"], sheet.Skills);   // the background's two, then the first two on the class list
        Assert.Equal(0, sheet.Xp);
        Assert.Contains("humanoid", sheet.Tags);
        Assert.Contains("class:rogue", sheet.Tags);   // the AI reads the class's aiTags by it
    }

    // ---- Picks ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Picks_grant_abilities_and_passives_chosen_from_the_choose_lists()
    {
        var picked = Rogue(7) with { Picks = [new AbilityId("test.mend"), new AbilityId("rogue.hardy")] };

        var sheet = SheetBuilder.ForCharacter(picked, Rules);

        Assert.Contains(new AbilityId("test.mend"), sheet.Abilities);
        Assert.DoesNotContain(new AbilityId("test.bolt"), sheet.Abilities);
        Assert.Contains("rogue.hardy", sheet.Passives);
    }

    [Fact]
    public void Empty_picks_fall_back_to_the_classes_default_picks()
    {
        // The default loadout at 2–6 is test.bolt; at 7 and above it is test.mend and rogue.hardy (a whole loadout, not an addition).
        var two = SheetBuilder.ForCharacter(Rogue(2), Rules);
        var seven = SheetBuilder.ForCharacter(Rogue(7), Rules);

        Assert.Contains(new AbilityId("test.bolt"), two.Abilities);
        Assert.DoesNotContain(new AbilityId("test.bolt"), seven.Abilities);
        Assert.Contains(new AbilityId("test.mend"), seven.Abilities);
        Assert.DoesNotContain(new AbilityId("test.bolt"), SheetBuilder.ForCharacter(Rogue(1), Rules).Abilities);
    }

    [Fact]
    public void Picks_outside_the_choose_lists_or_over_the_count_are_errors()
    {
        var notOffered = Rogue(7) with { Picks = [new AbilityId("test.potion")] };
        var tooMany = Rogue(2) with { Picks = [new AbilityId("test.bolt"), new AbilityId("test.mend")] };
        var notYet = Rogue(3) with { Picks = [new AbilityId("test.hold")] };       // offered only at level 7

        Assert.Equal([new ContentError("character 'Vex'", "/picks/0", "'test.potion' isn't one of the abilities this class offers a choice of at level 7.")],
            ErrorsOf(notOffered));
        Assert.Equal([new ContentError("character 'Vex'", "/picks", "A level 2 rogue chooses 1 ability, not 2.")], ErrorsOf(tooMany));
        Assert.Single(ErrorsOf(notYet));
    }

    [Fact]
    public void Picks_are_counted_per_choose_feature()
    {
        // Level 7 offers "1 of bolt, mend" (level 2) and "1 of hold, hardy" (level 7): two picks in all, but not two from one.
        var both = Rogue(7) with { Picks = [new AbilityId("test.bolt"), new AbilityId("test.mend")] };
        var spread = Rogue(7) with { Picks = [new AbilityId("test.mend"), new AbilityId("test.hold")] };

        Assert.Equal([new ContentError("character 'Vex'", "/picks/1",
                "'test.mend' is one pick too many: the level 2 choice takes 1 of test.bolt, test.mend.")],
            ErrorsOf(both));
        Assert.Contains(new AbilityId("test.hold"), SheetBuilder.ForCharacter(spread, Rules).Abilities);
    }

    // ---- Injuries ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Injury_applies_static_mods()
    {
        var healthy = SheetBuilder.ForCharacter(Rogue(), Rules);
        var ribs = SheetBuilder.ForCharacter(Rogue() with { Injuries = [new InjuryId("cracked_ribs")] }, Rules);
        var injured = SheetBuilder.ForCharacter(Rogue() with
        {
            Injuries = [new InjuryId("concussion"), new InjuryId("wrenched_shoulder"), new InjuryId("sprained_ankle")],
        }, Rules);

        // Max HP is baked into the sheet.
        Assert.Equal(healthy.MaxHp - 5, ribs.MaxHp);
        Assert.Equal(healthy.MaxHp, injured.MaxHp);

        // The rest reach the battle's modifiers like any passive's.
        var (state, id) = Started(injured);
        var mods = Modifiers.For(state, id, Rules);
        Assert.Equal(-1, mods.Attack);
        Assert.Equal(-1, mods.Saves![Stat.Int]);
        Assert.Equal(-1, mods.Saves[Stat.Wis]);
        Assert.Equal(-1, mods.Move);
        Assert.Equal(3, Modifiers.Move(state.Units[id].Sheet, mods));
        Assert.Equal(3, state.Units[id].Economy.MoveLeft);
        Assert.Equal(["injury:concussion", "injury:wrenched_shoulder", "injury:sprained_ankle"],
            injured.Passives.Where(p => p.StartsWith("injury:", StringComparison.Ordinal)));

        // And nothing is counted twice: Max HP is not re-applied by the modifiers, and a healthy unit has none.
        Assert.Equal(0, Modifiers.For(Started(ribs).State, id, Rules).MaxHp);
        Assert.Equal(StaticMods.None.Attack, Modifiers.For(Started(healthy).State, id, Rules).Attack);
    }

    [Fact]
    public void Max_hp_from_passives_is_applied_and_hp_never_drops_below_1()
    {
        var hardy = Rogue(7) with { Picks = [new AbilityId("test.mend"), new AbilityId("rogue.hardy")] };
        var fragile = Rogue() with { Injuries = [new InjuryId("cracked_ribs"), new InjuryId("cracked_ribs")] };

        Assert.Equal(SheetBuilder.ForCharacter(Rogue(7) with { Picks = [new AbilityId("test.mend")] }, Rules).MaxHp + 2,
            SheetBuilder.ForCharacter(hardy, Rules).MaxHp);
        Assert.Equal(1, SheetBuilder.ForCharacter(fragile, Rules).MaxHp);
    }

    // ---- Bad ids ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Unknown_race_throws_content_error()
    {
        var errors = ErrorsOf(Rogue(race: "gnomee"));

        Assert.Equal([new ContentError("character 'Vex'", "/race", "Unknown race 'gnomee'.")], errors);
    }

    [Fact]
    public void Every_bad_id_is_reported_at_once()
    {
        var c = Rogue(subclass: "rogue.shaddow") with
        {
            Class = "rogue",
            Background = "hermit",
            Weapon = "sword",
            Armor = "plate",
            Injuries = [new InjuryId("cracked_ribs"), new InjuryId("lost_leg")],
            Bag = [new ItemStack(new ItemId("potion"), 1), new ItemStack(new ItemId("elixir"), 1)],
            Picks = [new AbilityId("test.nothing")],
        };

        Assert.Equal(
        [
            new ContentError("character 'Vex'", "/subclass", "Unknown subclass 'rogue.shaddow' of rogue."),
            new ContentError("character 'Vex'", "/background", "Unknown background 'hermit'."),
            new ContentError("character 'Vex'", "/weapon", "Unknown weapon 'sword'."),
            new ContentError("character 'Vex'", "/armor", "Unknown armor 'plate'."),
            new ContentError("character 'Vex'", "/injuries/1", "Unknown injury 'lost_leg'."),
            new ContentError("character 'Vex'", "/bag/1/item", "Unknown item 'elixir'."),
            new ContentError("character 'Vex'", "/picks/0", "Unknown ability 'test.nothing'."),
        ], ErrorsOf(c));
    }

    [Fact]
    public void Unknown_class_and_bad_level_are_errors()
    {
        Assert.Equal([new ContentError("character 'Vex'", "/class", "Unknown class 'bard'.")], ErrorsOf(Rogue() with { Class = "bard" }));
        Assert.Equal([new ContentError("character 'Vex'", "/level", "Level 11 isn't between 1 and 10.")], ErrorsOf(Rogue(11)));
        Assert.Equal([new ContentError("character 'Vex'", "/level", "Level 0 isn't between 1 and 10.")], ErrorsOf(Rogue(0)));
    }

    // ---- Default characters -----------------------------------------------------------------------------------------

    [Fact]
    public void Default_character_assigns_the_standard_array_in_priority_order()
    {
        var rogue = SheetBuilder.DefaultCharacter("Vex", "human", "rogue", "rogue.shadow", 5, Rules);

        // Priority dex, int, con, wis, cha, str gets 15, 14, 13, 12, 10, 8 before the background.
        Assert.Equal(ImmutableSortedDictionary<Stat, int>.Empty
            .Add(Stat.Dex, 15).Add(Stat.Int, 14).Add(Stat.Con, 13).Add(Stat.Wis, 12).Add(Stat.Cha, 10).Add(Stat.Str, 8),
            rogue.BaseScores);
        Assert.Equal(("rapier", "leather", false), (rogue.Weapon, rogue.Armor, rogue.Shield));
        Assert.Equal([new ItemStack(new ItemId("potion"), 2)], rogue.Bag);
        Assert.Equal([new AbilityId("test.bolt")], rogue.Picks);
        Assert.Empty(rogue.Increases);
        Assert.Empty(rogue.Injuries);
        Assert.Equal(("Vex", "human", "rogue", "rogue.shadow", 5), (rogue.Name, rogue.Race, rogue.Class, rogue.Subclass, rogue.Level));
    }

    [Fact]
    public void Default_character_picks_the_background_that_suits_the_class()
    {
        // Scholar (+2 INT, +1 WIS) beats soldier (+2 STR, +1 CON) for a rogue whose priority is DEX, INT, CON, ….
        Assert.Equal("scholar", SheetBuilder.DefaultCharacter("Vex", "human", "rogue", null, 1, Rules).Background);
        Assert.Equal("soldier", SheetBuilder.DefaultCharacter("Brom", "human", "fighter", null, 1, Rules).Background);
        Assert.True(SheetBuilder.DefaultCharacter("Brom", "human", "fighter", null, 1, Rules).Shield);
    }

    [Fact]
    public void Default_characters_build_at_every_level()
    {
        foreach (var cls in new[] { "fighter", "rogue" })
            for (var level = 1; level <= 10; level++)
            {
                var sheet = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("X", "human", cls, null, level, Rules), Rules);
                Assert.Equal(level, sheet.Level);
            }
    }

    [Fact]
    public void Default_character_of_an_unknown_class_is_a_content_error()
    {
        var error = Assert.Single(Assert.Throws<ContentException>(
            () => SheetBuilder.DefaultCharacter("X", "human", "bard", null, 1, Rules)).Errors);

        Assert.Equal(new ContentError("character 'X'", "/class", "Unknown class 'bard'."), error);
    }

    // ---- Monsters ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Monster_sheet_is_the_stat_block()
    {
        var wolf = SheetBuilder.ForMonster("wolf", Rules);

        Assert.Equal(("Wolf", Side.Enemy, 1, 11, 13), (wolf.Name, wolf.Side, wolf.Level, wolf.MaxHp, wolf.Ac));
        Assert.Equal(Stat.Str, wolf.Weapon.Stat);
        Assert.Equal("bite", wolf.Weapon.Id);
        Assert.Equal(("brute", "beasts.wolf.grey", 50), (wolf.AiProfile, wolf.Sprite, wolf.Xp));
        Assert.Equal([new AbilityId("test.strike")], wolf.Abilities);
        Assert.Equal(["beast"], wolf.Tags);
        Assert.Null(wolf.CastingStat);
    }

    [Fact]
    public void Monster_level_override_changes_only_the_level()
    {
        var wolf = SheetBuilder.ForMonster("wolf", Rules, level: 4);

        Assert.Equal(4, wolf.Level);
        Assert.Equal(SheetBuilder.ForMonster("wolf", Rules) with { Level = 4 }, wolf);
    }

    [Fact]
    public void Unknown_monster_throws_content_error()
    {
        var error = Assert.Single(Assert.Throws<ContentException>(() => SheetBuilder.ForMonster("dragon", Rules)).Errors);

        Assert.Equal(new ContentError("monster 'dragon'", "", "Unknown monster 'dragon'."), error);
    }

    [Fact]
    public void Rules_pack_monster_sheet_is_the_sheet_builders()
    {
        Assert.Equal(SheetBuilder.ForMonster("wolf", Rules), Rules.MonsterSheet("wolf"));
        Assert.Null(Rules.MonsterSheet("dragon"));
        Assert.Same(TestSheets.Wolf, RulesPack.InMemory("t", monsters: [("wolf", TestSheets.Wolf)]).MonsterSheet("wolf"));
    }

    // ---- Content ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Weapons_and_armor_load_from_the_pack()
    {
        Assert.Equal(["longbow", "longsword", "quarterstaff", "rapier", "shortsword"], Rules.Weapons.Keys);
        Assert.Equal(["chain_mail", "leather", "none", "scale_mail"], Rules.Armors.Keys);
        Assert.Equal(new ArmorDef("scale_mail", "Scale mail", ArmorKind.Medium, 14), Rules.Armors["scale_mail"]);
        Assert.Equal(8, Rules.Weapons["longbow"].Range);
        Assert.Same(StringComparer.Ordinal, Rules.Weapons.KeyComparer);
    }
}
