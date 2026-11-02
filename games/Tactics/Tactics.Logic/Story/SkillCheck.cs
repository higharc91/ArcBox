using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A scene's skill check (lore §7, Checks; 5d plan §6): which stat a skill uses, a character's bonus, the best of the party, and the
/// roll. The bonus is the stat's modifier plus proficiency when the sheet has the skill, as for a save; no other rule adds to it. The
/// roll is <see cref="Rolls.Flat"/>, with no natural 20 or 1 (5d plan, default 7). The die is the caller's, so this stays clock- and
/// RNG-free.
/// </summary>
public static class SkillCheck
{
    /// <summary>The SRD's skills and the stat each uses: 18 lines of fact, no new maths (5d plan, flag 10).</summary>
    public static ImmutableSortedDictionary<string, Stat> Skills { get; } = new Dictionary<string, Stat>
    {
        ["athletics"] = Stat.Str,
        ["acrobatics"] = Stat.Dex,
        ["sleight_of_hand"] = Stat.Dex,
        ["stealth"] = Stat.Dex,
        ["arcana"] = Stat.Int,
        ["history"] = Stat.Int,
        ["investigation"] = Stat.Int,
        ["nature"] = Stat.Int,
        ["religion"] = Stat.Int,
        ["animal_handling"] = Stat.Wis,
        ["insight"] = Stat.Wis,
        ["medicine"] = Stat.Wis,
        ["perception"] = Stat.Wis,
        ["survival"] = Stat.Wis,
        ["deception"] = Stat.Cha,
        ["intimidation"] = Stat.Cha,
        ["performance"] = Stat.Cha,
        ["persuasion"] = Stat.Cha,
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The stat <paramref name="skill"/> uses, or null for a skill the SRD doesn't have.</summary>
    public static Stat? StatOf(string skill) => Skills.TryGetValue(skill, out var stat) ? stat : null;

    /// <summary>True when <paramref name="sheet"/> is proficient in <paramref name="skill"/>.</summary>
    public static bool Proficient(UnitSheet sheet, string skill) => sheet.Skills.Contains(skill);

    /// <summary>
    /// The bonus <paramref name="sheet"/> rolls <paramref name="skill"/> with: the stat's modifier, plus the proficiency bonus at its
    /// level when proficient. Throws for a skill with no stat.
    /// </summary>
    public static int Bonus(UnitSheet sheet, string skill)
    {
        var stat = StatOf(skill) ?? throw new ArgumentException($"'{skill}' isn't a skill.", nameof(skill));
        return Stats.Mod(sheet.Score(stat)) + (Proficient(sheet, skill) ? Stats.Proficiency(sheet.Level) : 0);
    }

    /// <summary>The bonus of the character <paramref name="c"/>, from the sheet <see cref="SheetBuilder.ForCharacter"/> builds.</summary>
    public static int Bonus(CharacterRecord c, string skill, RulesPack rules) => Bonus(SheetBuilder.ForCharacter(c, rules), skill);

    /// <summary>The member with the highest bonus, the first in <paramref name="cards"/>' order on a tie: who rolls when nobody picks.</summary>
    public static string Best(IEnumerable<(string Member, int Bonus)> cards)
    {
        (string Member, int Bonus)? best = null;
        foreach (var card in cards)
            if (best is null || card.Bonus > best.Value.Bonus)
                best = card;
        return best?.Member ?? throw new ArgumentException("Nobody can roll.", nameof(cards));
    }

    /// <summary>The check: <paramref name="die"/> (1–20) + <paramref name="bonus"/> against <paramref name="dc"/>.</summary>
    public static RollResult Roll(int die, int bonus, int dc) => Rolls.Flat(die, bonus, dc);
}
