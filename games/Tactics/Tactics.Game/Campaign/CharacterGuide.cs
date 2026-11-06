using System.Collections.Immutable;
using System.Globalization;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A race, as the portal's details sheet explains it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Facts">What it gives in play, a line each: speed, size, senses, resistances, skills, climbing.</param>
/// <param name="Traits">Its racial ability and passives, by feature id (<see cref="CharacterGuide.Feature"/>).</param>
public sealed record RaceGuide(string Name, ImmutableArray<string> Facts, ImmutableArray<string> Traits);

/// <summary>What a class gains at one level: features by id, how many attacks an Attack makes from here, and the choices to make.</summary>
public sealed record LevelGuide(int Level, ImmutableArray<string> Features, int? Attacks, ImmutableArray<ChoiceGuide> Choices);

/// <summary>A "choose <paramref name="Choose"/> of" feature, its options by feature id.</summary>
public sealed record ChoiceGuide(int Choose, ImmutableArray<string> From);

/// <summary>A subclass and what it gains by level.</summary>
public sealed record SubclassGuide(string Id, string Name, ImmutableArray<LevelGuide> Levels);

/// <summary>A class, as the portal's details sheet explains it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Role">Its role in the party, in words.</param>
/// <param name="Facts">Hit die and HP, saves, armour, weapons, skills, spellcasting, resources and kit, a line each.</param>
/// <param name="Levels">What it gains at each level from 1 to <see cref="CharacterGuide.UpTo"/>.</param>
/// <param name="Subclasses">Its subclasses, chosen at level 3, with what each gains by level.</param>
public sealed record ClassGuide(string Name, string Role, ImmutableArray<string> Facts, ImmutableArray<LevelGuide> Levels,
    ImmutableArray<SubclassGuide> Subclasses);

/// <summary>A background: its ability boosts, its skills (by id) and its keepsake.</summary>
public sealed record BackgroundGuide(string Name, ImmutableArray<string> Facts, ImmutableSortedDictionary<Stat, int> Boosts,
    ImmutableArray<string> Skills);

/// <summary>An ability or passive: its name, what it costs or limits (<paramref name="Tags"/>) and what it does.</summary>
public sealed record FeatureGuide(string Name, ImmutableArray<string> Tags, string Text);

/// <summary>A skill: its name, the ability score it uses and how it's rolled.</summary>
public sealed record SkillGuide(string Name, Stat Stat, string Text);

/// <summary>An ability score: its name and what it drives in play, a line each.</summary>
public sealed record StatGuide(string Name, ImmutableArray<string> Drives);

/// <summary>
/// Everything the portal's details sheets explain, sent once in the portal view and looked up by id (the user's note on the character
/// screens, 2026-11-01). Descriptions are keyed by id so a spell offered at three levels is written once.
/// </summary>
/// <param name="Level">The level the feature texts are worded for (dice that grow with level).</param>
public sealed record PortalDetails(int Level,
    ImmutableSortedDictionary<string, RaceGuide> Races, ImmutableSortedDictionary<string, ClassGuide> Classes,
    ImmutableSortedDictionary<string, BackgroundGuide> Backgrounds, ImmutableSortedDictionary<string, FeatureGuide> Features,
    ImmutableSortedDictionary<string, SkillGuide> Skills, ImmutableSortedDictionary<Stat, StatGuide> Stats);

/// <summary>
/// The words of the portal's details sheets, all from the rules pack and the engine's own rules (<see cref="SheetBuilder"/>,
/// <see cref="Tooltips"/>, <see cref="SkillCheck"/>), so they can't drift from what a battle does. Pure: no clock, no state.
/// </summary>
public static class CharacterGuide
{
    /// <summary>The highest level a class's progression is sent to for a character of <paramref name="level"/>: this level and the next.</summary>
    public static int UpTo(int level) => Math.Clamp(level + 1, 2, 10);

    /// <summary>
    /// The details for these races, classes and backgrounds, at <paramref name="level"/>: each class's progression to <see cref="UpTo"/>,
    /// and every feature any of them names. A subclass of <paramref name="fullSubclassesOf"/> (the member's own class) is sent to level
    /// 10, so a subclass is chosen knowing all it gives; the others stop at <see cref="UpTo"/> too.
    /// </summary>
    public static PortalDetails For(IEnumerable<RaceDef> races, IEnumerable<ClassDef> classes, IEnumerable<BackgroundDef> backgrounds,
        int level, RulesPack rules, string? fullSubclassesOf = null)
    {
        var upTo = UpTo(level);
        var featureIds = new SortedSet<string>(StringComparer.Ordinal);
        var raceGuides = races.ToImmutableSortedDictionary(r => r.Id, r => Race(r, rules, featureIds), StringComparer.Ordinal);
        var classGuides = classes.ToImmutableSortedDictionary(c => c.Id,
            c => Class(c, upTo, c.Id == fullSubclassesOf ? 10 : upTo, rules, featureIds), StringComparer.Ordinal);
        var backgroundGuides = backgrounds.ToImmutableSortedDictionary(b => b.Id, Background, StringComparer.Ordinal);
        var features = featureIds.Select(id => (id, Feature(id, level, rules))).Where(f => f.Item2 is not null)
            .ToImmutableSortedDictionary(f => f.id, f => f.Item2!, StringComparer.Ordinal);
        var skills = SkillCheck.Skills.ToImmutableSortedDictionary(s => s.Key, s => Skill(s.Key, s.Value, level), StringComparer.Ordinal);
        var stats = Enum.GetValues<Stat>().ToImmutableSortedDictionary(s => s, s => StatFor(s, rules));
        return new PortalDetails(level, raceGuides, classGuides, backgroundGuides, features, skills, stats);
    }

    // ---- Races ----------------------------------------------------------------------------------------------------

    /// <summary>A race's facts, in play terms, and its traits.</summary>
    public static RaceGuide Race(RaceDef race, RulesPack rules, ISet<string>? featureIds = null)
    {
        List<string> facts =
        [
            $"Speed: {Tiles(race.Move)} a turn.",
            $"Size: {TitleCase(race.Size)}{NotInBattles}.",
            race.Darkvision > 0
                ? $"Darkvision: {Tiles(race.Darkvision)}. Battles have no darkness yet, so it doesn't change a fight."
                : "Senses: normal sight.",
        ];
        if (!race.Resist.IsEmpty)
            facts.Add($"Resistant to {Join(race.Resist)} damage: takes half.");
        if (race.ExtraSkills > 0)
            facts.Add($"Skilled: {Count(race.ExtraSkills, "more skill", "more skills")} from your class's list.");
        if (race.Jump is { } jump)
            facts.Add(Climbs(jump));
        facts.Add("Ability boosts come from your background, not your race.");
        List<string> traits = [];
        if (race.Ability is { } ability)
            traits.Add(ability.Value);
        traits.AddRange(race.Passives);
        foreach (var t in traits)
            featureIds?.Add(t);
        return new RaceGuide(race.Name, [.. facts], [.. traits]);
    }

    // ---- Classes --------------------------------------------------------------------------------------------------

    /// <summary>A class's facts and its progression to <paramref name="upTo"/>, its subclasses' to <paramref name="subclassesTo"/>.</summary>
    public static ClassGuide Class(ClassDef cls, int upTo, int subclassesTo, RulesPack rules, ISet<string>? featureIds = null)
    {
        var hpLevel = (cls.HitDie + 1) / 2 + 1;
        List<string> facts =
        [
            $"Hit points: {cls.HitDie} + your CON modifier at level 1, then {hpLevel} + CON modifier each level (hit die d{cls.HitDie}).",
            $"Saving throws: {Join(cls.Saves.Select(StatName))}.",
            cls.ArmorProf.IsDefaultOrEmpty ? $"Armour: none{NotInBattles}." : $"Armour: {Join(cls.ArmorProf.Select(Armor))}{NotInBattles}.",
            cls.WeaponProf.IsDefaultOrEmpty ? $"Weapons: none{NotInBattles}." : $"Weapons: {Join(cls.WeaponProf.Select(w => WeaponProf(w, rules)))}{NotInBattles}.",
            $"Skills: {Count(cls.SkillChoices.Count, "skill", "skills")} from {Join(cls.SkillChoices.From.Select(SkillName), "or")}, the first not already from your background.",
        ];
        if (cls.CastingStat is { } casting)
            facts.Add($"Spellcasting: {StatName(casting)}. Spell attacks add its modifier, and your spells' save DC is 8 + proficiency + its modifier.");
        if (cls.UnarmoredDefense is { } unarmored)
            facts.Add($"Unarmoured defence: without armour, Armour Class adds your {Short(unarmored)} modifier.");
        foreach (var (id, table) in cls.Resources)
            facts.Add($"{TitleCase(id.Replace('_', ' '))}: {table.At(Math.Clamp(upTo - 1, 1, 10))} per battle at level {Math.Clamp(upTo - 1, 1, 10)}, {table.At(10)} by level 10.");
        if (cls.Jump != ClassDef.DefaultJump)
            facts.Add(Climbs(cls.Jump));
        var weapon = rules.Weapons.TryGetValue(cls.StartingKit.Weapon, out var w) ? Words(w.Id) : Words(cls.StartingKit.Weapon);
        var armor = rules.Armors.TryGetValue(cls.StartingKit.Armor, out var a) ? a.Name.ToLowerInvariant() : Words(cls.StartingKit.Armor);
        facts.Add($"Starts with: {weapon}{(string.IsNullOrEmpty(cls.StartingKit.Armor) ? "" : $", {armor}")}{(cls.StartingKit.Shield ? ", a shield" : "")}.");

        var levels = Levels(cls.Features, upTo, featureIds);
        var subclasses = cls.Subclasses.Select(s => new SubclassGuide(s.Id, s.Name, Levels(s.Features, subclassesTo, featureIds))).ToImmutableArray();
        return new ClassGuide(cls.Name, Role(cls.Role), [.. facts], levels, subclasses);
    }

    private static ImmutableArray<LevelGuide> Levels(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> features, int upTo, ISet<string>? ids)
    {
        var levels = ImmutableArray.CreateBuilder<LevelGuide>();
        foreach (var (level, list) in features)
        {
            if (level > upTo || list.IsDefaultOrEmpty)
                continue;
            List<string> named = [];
            List<ChoiceGuide> choices = [];
            int? attacks = null;
            foreach (var f in list)
            {
                if (f.Ability is { } ability)
                    named.Add(ability.Value);
                else if (f.Passive is { } passive)
                    named.Add(passive);
                else if (f.Attacks is { } n)
                    attacks = n;
                else if (f.Choose is { } choose && !f.From.IsDefaultOrEmpty)
                    choices.Add(new ChoiceGuide(choose, f.From));
            }
            foreach (var id in named.Concat(choices.SelectMany(c => c.From)))
                ids?.Add(id);
            levels.Add(new LevelGuide(level, [.. named], attacks, [.. choices]));
        }
        return levels.ToImmutable();
    }

    private static string Role(string role) => role switch
    {
        "healer" => "Healer: keeps the party standing.",
        "defender" => "Defender: stands in front and takes the hits.",
        "striker" => "Striker: deals the most damage to one foe.",
        "control" => "Control: shapes the fight with areas and conditions.",
        _ => $"{TitleCase(role)}.",
    };

    /// <summary>
    /// The rule for facts the pack holds but no battle reads yet (size, armour and weapon proficiencies, a keepsake): they're shown, with
    /// this said after them, as darkvision says it doesn't change a fight.
    /// </summary>
    public const string NotInBattles = " (not used in battles yet)";

    /// <summary>"Climbs 3 levels in one step (most climb 2)": the Jump a battle's movement uses.</summary>
    private static string Climbs(int jump)
        => $"Climbs {Count(jump, "level", "levels")} in one step (most climb {ClassDef.DefaultJump}).";

    /// <summary>
    /// A weapon proficiency: a category, every part of it a weapon tag ("martial_light" → "light martial weapons"), or else a weapon by
    /// its name ("hand_crossbow" → "hand crossbow").
    /// </summary>
    private static string WeaponProf(string prof, RulesPack rules)
        => prof.Split('_').All(part => rules.Weapons.Values.Any(w => w.Tags.Contains(part)))
            ? $"{string.Join(' ', prof.Split('_').Reverse())} weapons"
            : Words(prof);

    private static string Armor(string prof) => prof switch
    {
        "shield" => "shields",
        _ => $"{prof} armour",
    };

    // ---- Backgrounds ----------------------------------------------------------------------------------------------

    /// <summary>A background's boosts, skills and keepsake, in words.</summary>
    public static BackgroundGuide Background(BackgroundDef b)
    {
        List<string> facts =
        [
            b.Boosts.IsEmpty ? "No ability boosts." : $"Ability boosts: {Join(b.Boosts.Select(x => $"{Short(x.Key)} +{x.Value}"))}.",
            $"Skills: {Join(b.Skills.Select(SkillName))}.",
        ];
        if (!string.IsNullOrEmpty(b.Trinket))
            facts.Add($"Keepsake: {b.Trinket}{NotInBattles}.");
        return new BackgroundGuide(b.Name, [.. facts], b.Boosts, b.Skills.IsDefault ? [] : b.Skills);
    }

    // ---- Features -------------------------------------------------------------------------------------------------

    /// <summary>
    /// An ability or passive by id, at <paramref name="level"/>: its name, its cost and limits, and the engine's tooltip for it. Null for
    /// an id the pack doesn't have.
    /// </summary>
    public static FeatureGuide? Feature(string id, int level, RulesPack rules)
    {
        if (rules.Abilities.TryGetValue(new AbilityId(id), out var a))
            return new FeatureGuide(a.Name, Cost(a), Tooltips.For(a, level, rules));
        if (rules.Passives.TryGetValue(id, out var p))
        {
            List<string> tags = ["Always on"];
            if (p.UsesPerBattle is { } uses)
                tags[0] = uses == 1 ? "Once per battle" : $"{uses} times per battle";
            return new FeatureGuide(TitleCase(Words(id)), [.. tags], TitleCase(Tooltips.For(p, level, rules)));
        }
        return null;
    }

    /// <summary>What using an ability takes: "Action", "2 mana", "Concentration", "Tier 1 spell", "2 uses per battle".</summary>
    public static ImmutableArray<string> Cost(AbilityDef a)
    {
        List<string> tags = [];
        if (a.Tier > 0)
            tags.Add($"Tier {a.Tier} spell");
        if (a.Cost.Action > 0)
            tags.Add("Action");
        if (a.Cost.Bonus > 0)
            tags.Add("Bonus action");
        if (a.Cost.Reaction > 0)
            tags.Add("Reaction");
        if (a.Cost.Move > 0)
            tags.Add($"{Tiles(a.Cost.Move)} of move");
        foreach (var (resource, amount) in a.Cost.Resource ?? ImmutableSortedDictionary<string, int>.Empty)
            tags.Add($"{amount} {Words(resource)}");
        if (a.Cost.Uses is { } uses)
            tags.Add(uses == 1 ? "Once per battle" : $"{uses} uses per battle");
        if (a.Cost.Concentration)
            tags.Add("Concentration");
        if (tags.Count == 0 || tags.All(t => t.StartsWith("Tier", StringComparison.Ordinal)))
            tags.Add("Free");
        return [.. tags];
    }

    /// <summary>A skill, and how a story check rolls it (<see cref="SkillCheck"/>): a d20, the stat's modifier and proficiency if you have it.</summary>
    public static SkillGuide Skill(string id, Stat stat, int level)
        => new(SkillName(id), stat,
            $"Story checks: d20 + {Short(stat)} modifier, + {Stats.Proficiency(level)} if proficient, against the DC.");

    // ---- Ability scores -------------------------------------------------------------------------------------------

    /// <summary>What an ability score drives, from the engine's rules and the pack: weapons, armour, HP, initiative, spells, saves, skills.</summary>
    public static StatGuide StatFor(Stat stat, RulesPack rules)
    {
        List<string> drives = ["Its modifier is (score − 10) ÷ 2, rounded down: 8 is −1, 10 is +0, 14 is +2, 20 is +5."];
        var weapons = rules.Weapons.Values.Where(w => w.Stat == stat && !w.Finesse).Select(w => Words(w.Id)).ToList();
        if (weapons.Count > 0)
            drives.Add($"Attack and damage rolls with {Join(weapons)}.");
        if (stat is Stat.Str or Stat.Dex)
        {
            var finesse = rules.Weapons.Values.Where(w => w.Finesse).Select(w => Words(w.Id)).ToList();
            if (finesse.Count > 0)
                drives.Add($"Finesse weapons ({Join(finesse)}) use whichever of STR and DEX is higher.");
        }
        switch (stat)
        {
            case Stat.Dex:
                drives.Add("Armour Class: all of the modifier in light armour or none, at most +2 in medium, none in heavy.");
                drives.Add("Initiative: 10 + the modifier decides who acts first (heavy armour takes 2).");
                break;
            case Stat.Con:
                drives.Add("Hit points: the modifier is added at level 1 and again at every level.");
                break;
        }
        var unarmored = rules.Classes.Values.Where(c => c.UnarmoredDefense == stat).Select(c => c.Name).ToList();
        if (unarmored.Count > 0)
            drives.Add($"Armour Class without armour, for a {Join(unarmored, "or")}.");
        var casters = rules.Classes.Values.Where(c => c.CastingStat == stat).Select(c => c.Name).ToList();
        if (casters.Count > 0)
            drives.Add($"Spell attacks and spell save DCs for a {Join(casters, "or")}.");
        var racial = rules.Races.Values.Where(r => r.Ability is { } id && rules.Abilities.TryGetValue(id, out var a) && SaveDcStat(a) == stat)
            .Select(r => $"{rules.Abilities[r.Ability!.Value].Name} ({r.Name})").ToList();
        if (racial.Count > 0)
            drives.Add($"The save DC of {Join(racial)}.");
        var saving = rules.Classes.Values.Where(c => c.Saves.Contains(stat)).Select(c => c.Name).ToList();
        drives.Add($"{Short(stat)} saving throws, against effects that call for one{(saving.Count > 0 ? $"; proficient for a {Join(saving, "or")}" : "")}.");
        var skills = SkillCheck.Skills.Where(s => s.Value == stat).Select(s => SkillName(s.Key)).ToList();
        drives.Add(skills.Count > 0 ? $"Skill checks in the story: {Join(skills)}." : "No skills use it.");
        return new StatGuide(StatName(stat), [.. drives]);
    }

    private static Stat? SaveDcStat(AbilityDef a)
        => a.Effects.IsDefaultOrEmpty ? null : a.Effects.OfType<SaveEffect>().Select(s => s.DcStat).FirstOrDefault(s => s is not null);

    // ---- Words ----------------------------------------------------------------------------------------------------

    /// <summary>"Strength".</summary>
    public static string StatName(Stat stat) => stat switch
    {
        Stat.Str => "Strength",
        Stat.Dex => "Dexterity",
        Stat.Con => "Constitution",
        Stat.Int => "Intelligence",
        Stat.Wis => "Wisdom",
        _ => "Charisma",
    };

    /// <summary>"STR".</summary>
    public static string Short(Stat stat) => stat.ToString().ToUpperInvariant();

    /// <summary>"sleight_of_hand" → "Sleight of Hand".</summary>
    public static string SkillName(string id)
        => string.Join(' ', id.Split('_').Select((w, i) => i > 0 && w is "of" or "the" ? w : TitleCase(w)));

    private static string Tiles(int n) => $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? "tile" : "tiles")}";

    private static string Count(int n, string one, string many) => $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";

    private static string TitleCase(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"rogue.sneak_attack" → "sneak attack".</summary>
    private static string Words(string id) => id[(id.LastIndexOf('.') + 1)..].Replace('_', ' ');

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string Join(IEnumerable<string> items, string and = "and")
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => "",
            1 => list[0],
            _ => $"{string.Join(", ", list[..^1])} {and} {list[^1]}",
        };
    }
}
