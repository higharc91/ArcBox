using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A player's character as the portal stores it (spec §3.1): who they are and what they chose. <see cref="BaseScores"/> are
/// the point-buy scores (8–15) before the background; <see cref="Increases"/> are the ability score increases spent at
/// levels 4 and 8; <see cref="Picks"/> are the abilities and passives chosen from the class's <c>choose</c> features (spells
/// known and the like; empty means the class's default loadout); <see cref="Injuries"/> are those not yet healed.
/// <see cref="Weapon"/> and <see cref="Armor"/> are ids from <c>weapons.json</c> and <c>armor.json</c> (an empty
/// <see cref="Armor"/> is no armour), and <see cref="Shield"/> adds +2 AC.
/// </summary>
public sealed record CharacterRecord(string Name, string Race, string Class, string? Subclass, string Background,
    int Level, ImmutableSortedDictionary<Stat, int> BaseScores, ImmutableSortedDictionary<Stat, int> Increases,
    ImmutableArray<AbilityId> Picks, ImmutableArray<InjuryId> Injuries, string Weapon, string Armor, bool Shield,
    ImmutableArray<ItemStack> Bag);

/// <summary>
/// Builds the <see cref="UnitSheet"/>s a battle starts from: a character from its <see cref="CharacterRecord"/> (spec §3.1)
/// and a monster from its stat block (spec §4.7). A bad id in a record is a <see cref="ContentException"/> naming
/// <c>"character '&lt;name&gt;'"</c> and the JSON pointer of the field.
/// </summary>
public static class SheetBuilder
{
    /// <summary>The prefix of the sheet tag that names a character's class: <c>"class:cleric"</c>.</summary>
    public const string ClassTag = "class:";

    private const int MaxLevel = 10;
    private const int MaxScore = 20;
    private static readonly int[] StandardArray = [15, 14, 13, 12, 10, 8];

    // ---- Characters -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The sheet of <paramref name="c"/>:
    /// <list type="bullet">
    /// <item>Scores are the base scores plus the background's boosts and the increases, at most 20 each.</item>
    /// <item>Max HP is the hit die plus CON at level 1, then ⌈die / 2⌉ + 1 + CON per level, plus the
    /// <see cref="StaticMods.MaxHp"/> of the sheet's passives and injuries (never below 1).</item>
    /// <item>AC is the armour's base plus DEX (light: all of it, medium: at most +2, heavy: none; no armour is 10) and +2 for a
    /// shield. The sheet's initiative bonus stays 0: heavy armour's −2 is applied by <see cref="TurnOrder.Initiative"/>.</item>
    /// <item>Move is the race's; Jump is the race's, if it sets one, else the class's. Resistances are the race's.</item>
    /// <item>Abilities and passives are the racial ones, the class's features up to the level (and attacks per Attack action),
    /// the chosen subclass's features up to the level (at 3, 6 and 10), the picks (<see cref="CharacterRecord.Picks"/>, or
    /// the class's default loadout when empty), and one <see cref="RulesPack.InjuryPassiveId"/> passive per injury.
    /// A <c>resourceRegen</c> feature isn't part of the sheet.</item>
    /// <item>Resources are the class's tables at the level, with its primary resource. Saves are the class's. Skills are the
    /// background's two and the first few of the class's list (its skill choice count, plus the race's extra skills).</item>
    /// <item>Tags are <c>humanoid</c>, the race id and the class (<see cref="ClassTag"/> + its id, which the AI reads the
    /// class's <c>aiTags</c> by). There is no sprite: the portal draws a character from its creator look.</item>
    /// </list>
    /// Throws <see cref="ContentException"/> with every bad id: race, class, subclass, background, weapon, armour, injuries,
    /// bag items and picks, and a level outside 1–10. A pick must be offered by a <c>choose</c> feature of the class or
    /// subclass at or below the level, and each of those features takes no more than its own count (<see cref="PickRules"/>).
    /// </summary>
    public static UnitSheet ForCharacter(CharacterRecord c, RulesPack rules)
    {
        var problems = new ContentProblems($"character '{c.Name}'");
        rules.Races.TryGetValue(c.Race, out var race);
        rules.Classes.TryGetValue(c.Class, out var cls);
        rules.Backgrounds.TryGetValue(c.Background, out var background);
        if (race is null)
            problems.Add("/race", $"Unknown race '{c.Race}'.");
        if (cls is null)
            problems.Add("/class", $"Unknown class '{c.Class}'.");
        SubclassDef? subclass = null;
        if (c.Subclass is { } subclassId && cls is not null
            && (subclass = cls.Subclasses.FirstOrDefault(s => s.Id == subclassId)) is null)
            problems.Add("/subclass", $"Unknown subclass '{subclassId}' of {cls.Id}.");
        if (background is null)
            problems.Add("/background", $"Unknown background '{c.Background}'.");
        var levelOk = c.Level is >= 1 and <= MaxLevel;
        if (!levelOk)
            problems.Add("/level", $"Level {c.Level} isn't between 1 and {MaxLevel}.");

        WeaponDef? weapon = null;
        if (!rules.Weapons.TryGetValue(c.Weapon, out weapon))
            problems.Add("/weapon", $"Unknown weapon '{c.Weapon}'.");
        ArmorDef? armor = null;
        if (c.Armor.Length > 0 && !rules.Armors.TryGetValue(c.Armor, out armor))
            problems.Add("/armor", $"Unknown armor '{c.Armor}'.");
        var injuries = new List<InjuryDef>();
        var recordedInjuries = c.Injuries.IsDefault ? [] : c.Injuries;
        for (var i = 0; i < recordedInjuries.Length; i++)
            if (rules.Injuries.TryGetValue(recordedInjuries[i], out var injury))
                injuries.Add(injury);
            else
                problems.Add($"/injuries/{i}", $"Unknown injury '{recordedInjuries[i]}'.");
        var bag = c.Bag.IsDefault ? [] : c.Bag;
        for (var i = 0; i < bag.Length; i++)
            if (!rules.Items.ContainsKey(bag[i].Item))
                problems.Add($"/bag/{i}/item", $"Unknown item '{bag[i].Item}'.");

        var picks = c.Picks.IsDefaultOrEmpty ? (cls is not null && levelOk ? DefaultPicks(cls, c.Level) : []) : c.Picks;
        if (cls is not null && levelOk)
            CheckPicks(picks, cls, subclass, c.Level, rules, problems);
        problems.ThrowIfAny();

        return Build(c, race!, cls!, subclass, background!, weapon!, armor, [.. injuries], picks, rules);
    }

    private static UnitSheet Build(CharacterRecord c, RaceDef race, ClassDef cls, SubclassDef? subclass, BackgroundDef background,
        WeaponDef weapon, ArmorDef? armor, ImmutableArray<InjuryDef> injuries, ImmutableArray<AbilityId> picks, RulesPack rules)
    {
        var scores = ImmutableSortedDictionary.CreateBuilder<Stat, int>();
        foreach (var stat in Enum.GetValues<Stat>())
            scores[stat] = Math.Min(MaxScore, c.BaseScores.GetValueOrDefault(stat, 10) + background.Boosts.GetValueOrDefault(stat)
                                              + c.Increases.GetValueOrDefault(stat));
        var finalScores = scores.ToImmutable();

        // Features: the race's, then each level's of the class and the subclass, then the picks.
        var abilities = new List<AbilityId>();
        var passives = new List<string>(race.Passives);
        var attacks = 1;
        if (race.Ability is { } racial)
            abilities.Add(racial);
        for (var level = 1; level <= c.Level; level++)
            foreach (var feature in Features(cls, level).Concat(subclass is null ? [] : Features(subclass, level)))
            {
                if (feature.Ability is { } ability)
                    abilities.Add(ability);
                else if (feature.Passive is { } passive)
                    passives.Add(passive);
                else if (feature.Attacks is { } n)
                    attacks = Math.Max(attacks, n);
            }
        foreach (var pick in picks)
            if (rules.Abilities.ContainsKey(pick))
                abilities.Add(pick);
            else
                passives.Add(pick.Value);

        var maxHp = MaxHp(cls.HitDie, c.Level, Stats.Mod(finalScores[Stat.Con]))
                    + passives.Distinct().Sum(p => rules.Passives.GetValueOrDefault(p)?.Static?.MaxHp ?? 0)
                    + injuries.Sum(i => i.Static.MaxHp);
        passives.AddRange(injuries.Select(i => RulesPack.InjuryPassiveId(i.Id)));

        var skills = new List<string>(background.Skills);
        skills.AddRange(cls.SkillChoices.From.Where(s => !skills.Contains(s)).Take(cls.SkillChoices.Count + race.ExtraSkills));

        var unarmoredMod = cls.UnarmoredDefense is { } s ? Stats.Mod(finalScores[s]) : 0;

        return new UnitSheet(
            Name: c.Name, Side: Side.Party, Level: c.Level, Scores: finalScores, MaxHp: Math.Max(1, maxHp),
            Ac: ArmorClass(armor, c.Shield, Stats.Mod(finalScores[Stat.Dex]), unarmoredMod), Armor: armor?.Kind ?? ArmorKind.None,
            Move: race.Move, Jump: race.Jump ?? cls.Jump, InitiativeBonus: 0, SaveProficiencies: [.. cls.Saves],
            Skills: Ordinal.Set(skills),
            Resources: Ordinal.Map(cls.Resources.Select(r => (r.Key, r.Value.At(c.Level)))),
            Abilities: [.. abilities.Distinct()], Passives: [.. passives.Distinct()], Weapon: weapon, Attacks: attacks,
            Resist: race.Resist, Immune: Ordinal.Set(), Vulnerable: Ordinal.Set(), Tags: Ordinal.Set("humanoid", race.Id, ClassTag + cls.Id),
            Sprite: null, AiProfile: null, CastingStat: cls.CastingStat?.ToString(), PrimaryResource: cls.PrimaryResource);
    }

    private static IEnumerable<FeatureRef> Features(ClassDef cls, int level)
        => cls.Features.TryGetValue(level, out var list) ? list : [];

    private static IEnumerable<FeatureRef> Features(SubclassDef subclass, int level)
        => subclass.Features.TryGetValue(level, out var list) ? list : [];

    /// <summary>The hit die plus CON at level 1, then ⌈die / 2⌉ + 1 + CON for each level after it.</summary>
    private static int MaxHp(int hitDie, int level, int con) => hitDie + con + (level - 1) * ((hitDie + 1) / 2 + 1 + con);

    private static int ArmorClass(ArmorDef? armor, bool shield, int dexMod, int unarmoredMod)
    {
        var (baseAc, dex) = armor switch
        {
            null => (10 + unarmoredMod, dexMod),
            { Kind: ArmorKind.None } => (armor.BaseAc + unarmoredMod, dexMod),
            { Kind: ArmorKind.Medium } => (armor.BaseAc, Math.Min(dexMod, 2)),
            { Kind: ArmorKind.Heavy } => (armor.BaseAc, 0),
            _ => (armor.BaseAc, dexMod),
        };
        return baseAc + dex + (shield ? 2 : 0);
    }

    // ---- Picks ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The class's default loadout at <paramref name="level"/>: the whole list of the highest <c>defaultPicks</c> level at or
    /// below it (a loadout, not an addition to the lower ones), or nothing when there is none.
    /// </summary>
    private static ImmutableArray<AbilityId> DefaultPicks(ClassDef cls, int level)
        => cls.DefaultPicks.Where(p => p.Key <= level).Select(p => p.Value).LastOrDefault() is { IsDefault: false } picks ? picks : [];

    private static void CheckPicks(ImmutableArray<AbilityId> picks, ClassDef cls, SubclassDef? subclass, int level, RulesPack rules,
        ContentProblems problems)
    {
        if (picks.IsDefaultOrEmpty)
            return;
        for (var i = 0; i < picks.Length; i++)
            if (!rules.Abilities.ContainsKey(picks[i]) && !rules.Passives.ContainsKey(picks[i].Value))
                problems.Add($"/picks/{i}", $"Unknown ability '{picks[i]}'.");
        foreach (var (pointer, message) in PickRules.Check(picks, PickRules.Choices(cls, subclass, level), cls.Id, level,
                     pick => rules.Abilities.ContainsKey(pick) || rules.Passives.ContainsKey(pick.Value)))
            problems.Add("/picks" + pointer, message);
    }

    // ---- Default characters -----------------------------------------------------------------------------------------

    /// <summary>
    /// A ready-made character of the class: the standard array (15, 14, 13, 12, 10, 8) assigned in the class's
    /// <see cref="ClassAiTags.Priority"/> order, the background whose boosts suit that order best (ties by id), the class's
    /// starting kit and default picks at <paramref name="level"/>, no increases (the AI never spends them) and no injuries.
    /// For the simulator, AI-built characters and tests. Throws <see cref="ContentException"/> for an unknown class or a pack
    /// with no backgrounds; the race and the subclass are checked when the sheet is built.
    /// </summary>
    public static CharacterRecord DefaultCharacter(string name, string race, string cls, string? subclass, int level, RulesPack rules)
    {
        var problems = new ContentProblems($"character '{name}'");
        if (!rules.Classes.TryGetValue(cls, out var def))
            problems.Add("/class", $"Unknown class '{cls}'.");
        if (rules.Backgrounds.IsEmpty)
            problems.Add("/background", "The rules pack has no backgrounds.");
        problems.ThrowIfAny();

        var priority = def!.AiTags.Priority;
        var scores = ImmutableSortedDictionary.CreateBuilder<Stat, int>();
        for (var i = 0; i < priority.Length; i++)
            scores[priority[i]] = StandardArray[i];
        var background = rules.Backgrounds.Values
            .OrderByDescending(b => b.Boosts.Sum(boost => boost.Value * (priority.Length - priority.IndexOf(boost.Key))))
            .ThenBy(b => b.Id, StringComparer.Ordinal)
            .First();

        return new CharacterRecord(name, race, cls, subclass, background.Id, level, scores.ToImmutable(),
            ImmutableSortedDictionary<Stat, int>.Empty, level is >= 1 and <= MaxLevel ? DefaultPicks(def, level) : [], [],
            def.StartingKit.Weapon, def.StartingKit.Armor, def.StartingKit.Shield, def.StartingKit.Bag);
    }

    // ---- Monsters ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The sheet of a monster's stat block, on the enemy side: its scores, HP, AC, weapon and the rest as written, at its own
    /// level or <paramref name="level"/> (which changes only <see cref="UnitSheet.Level"/>, and with it what scales by
    /// level; the stat block isn't rescaled). Throws <see cref="ContentException"/> for an unknown monster or a level
    /// below 1. <see cref="RulesPack.MonsterSheet"/> is the lookup summons and named hooks use.
    /// </summary>
    public static UnitSheet ForMonster(string monsterId, RulesPack rules, int? level = null)
    {
        var problems = new ContentProblems($"monster '{monsterId}'");
        rules.Monsters.TryGetValue(monsterId, out var m);
        if (m is null)
            problems.Add("", $"Unknown monster '{monsterId}'.");
        if (level < 1)
            problems.Add("/level", $"Level {level} isn't at least 1.");
        problems.ThrowIfAny();

        return new UnitSheet(m!.Name, Side.Enemy, level ?? m.Level, m.Scores, m.Hp, m.Ac, m.Armor, m.Move, m.Jump,
            m.InitiativeBonus, m.Saves, m.Skills, m.Resources, m.Abilities, m.Passives, m.Weapon, m.Attacks, m.Resist, m.Immune,
            m.Vulnerable, m.Tags, m.Sprite, m.AiProfile, m.CastingStat?.ToString(), m.Xp);
    }
}
