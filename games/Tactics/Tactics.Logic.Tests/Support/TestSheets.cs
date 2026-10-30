using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>
/// Hand-built level-1 sheets with fixed numbers. Initiative (10 + DEX mod + bonus, heavy armour −2):
/// fighter 9, rogue 13, cleric 10, goblin 12.
/// </summary>
public static class TestSheets
{
    /// <summary>STR 16, DEX 12, CON 14; chain mail and shield (AC 18, heavy); 12 HP; longsword.</summary>
    public static readonly UnitSheet Fighter = new(
        Name: "Fighter", Side: Side.Party, Level: 1, Scores: Scores(16, 12, 14, 8, 10, 10),
        MaxHp: 12, Ac: 18, Armor: ArmorKind.Heavy, Move: 4, Jump: 2, InitiativeBonus: 0,
        SaveProficiencies: [Stat.Str, Stat.Con], Skills: Ordinal.Set("athletics", "perception"),
        Resources: Ordinal.Map(("second_wind", 1)), Abilities: [], Passives: [],
        Weapon: Weapon("longsword", "1d8", "slashing", Stat.Str), Attacks: 1,
        Resist: Ordinal.Set(), Immune: Ordinal.Set(), Vulnerable: Ordinal.Set(), Tags: Ordinal.Set("humanoid"),
        Sprite: "fighter", AiProfile: null, CastingStat: null);

    /// <summary>DEX 16, CON 12; leather (AC 14, light); 9 HP; shortsword (finesse); Jump 3.</summary>
    public static readonly UnitSheet Rogue = new(
        Name: "Rogue", Side: Side.Party, Level: 1, Scores: Scores(8, 16, 12, 13, 10, 14),
        MaxHp: 9, Ac: 14, Armor: ArmorKind.Light, Move: 4, Jump: 3, InitiativeBonus: 0,
        SaveProficiencies: [Stat.Dex, Stat.Int], Skills: Ordinal.Set("acrobatics", "stealth"),
        Resources: Ordinal.Map<int>(), Abilities: [], Passives: [],
        Weapon: Weapon("shortsword", "1d6", "piercing", Stat.Dex, finesse: true), Attacks: 1,
        Resist: Ordinal.Set(), Immune: Ordinal.Set(), Vulnerable: Ordinal.Set(), Tags: Ordinal.Set("humanoid"),
        Sprite: "rogue", AiProfile: null, CastingStat: null);

    /// <summary>WIS 16, DEX 10, CON 14; scale mail and shield (AC 16, medium); 10 HP; mace; 4 Mana, 1 Devotion.</summary>
    public static readonly UnitSheet Cleric = new(
        Name: "Cleric", Side: Side.Party, Level: 1, Scores: Scores(12, 10, 14, 10, 16, 8),
        MaxHp: 10, Ac: 16, Armor: ArmorKind.Medium, Move: 4, Jump: 2, InitiativeBonus: 0,
        SaveProficiencies: [Stat.Wis, Stat.Cha], Skills: Ordinal.Set("medicine", "religion"),
        Resources: Ordinal.Map(("mana", 4), ("devotion", 1)), Abilities: [], Passives: [],
        Weapon: Weapon("mace", "1d6", "bludgeoning", Stat.Str), Attacks: 1,
        Resist: Ordinal.Set(), Immune: Ordinal.Set(), Vulnerable: Ordinal.Set(), Tags: Ordinal.Set("humanoid"),
        Sprite: "cleric", AiProfile: null, CastingStat: "Wis");

    /// <summary>DEX 14; leather and shield (AC 15, light); 7 HP; scimitar (finesse); worth 50 XP.</summary>
    public static readonly UnitSheet Goblin = new(
        Name: "Goblin", Side: Side.Enemy, Level: 1, Scores: Scores(8, 14, 10, 10, 8, 8),
        MaxHp: 7, Ac: 15, Armor: ArmorKind.Light, Move: 4, Jump: 2, InitiativeBonus: 0,
        SaveProficiencies: [], Skills: Ordinal.Set("stealth"),
        Resources: Ordinal.Map<int>(), Abilities: [], Passives: [],
        Weapon: Weapon("scimitar", "1d6", "slashing", Stat.Dex, finesse: true), Attacks: 1,
        Resist: Ordinal.Set(), Immune: Ordinal.Set(), Vulnerable: Ordinal.Set(), Tags: Ordinal.Set("goblinoid"),
        Sprite: "goblin", AiProfile: "brute", CastingStat: null, Xp: 50);

    /// <summary>The six scores in STR, DEX, CON, INT, WIS, CHA order.</summary>
    public static ImmutableSortedDictionary<Stat, int> Scores(int str, int dex, int con, int @int, int wis, int cha)
        => new Dictionary<Stat, int>
        {
            [Stat.Str] = str, [Stat.Dex] = dex, [Stat.Con] = con, [Stat.Int] = @int, [Stat.Wis] = wis, [Stat.Cha] = cha,
        }.ToImmutableSortedDictionary();

    /// <summary>The sheet with one score changed.</summary>
    public static UnitSheet WithScore(this UnitSheet sheet, Stat stat, int score)
        => sheet with { Scores = sheet.Scores.SetItem(stat, score) };

    private static WeaponDef Weapon(string id, string damage, string type, Stat stat, bool finesse = false)
        => new(id, Dice.Parse(damage), type, stat, finesse, Ranged: false, Range: 1, Reach: false, Tags: Ordinal.Set());
}
