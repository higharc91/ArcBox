using System.Collections.Immutable;
using System.Globalization;

namespace Tactics.Logic;

/// <summary>
/// The wording pieces tooltips are built from (spec §4.1 point 4): numbers, durations, condition names, areas, modifiers
/// and hook conditions. Kept apart from <see cref="Tooltips"/>, which assembles headers, so every phrase lives in one place.
/// </summary>
internal static class TooltipText
{
    /// <summary><c>"1 tile"</c>, <c>"6 tiles"</c>.</summary>
    public static string Tiles(int n) => $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? "tile" : "tiles")}";

    /// <summary><c>"for 1 round"</c>, <c>"for 3 rounds"</c>, or for 0 <c>"until the end of your turn"</c>.</summary>
    public static string ForRounds(int rounds)
        => rounds <= 0 ? "until the end of your turn" : $"for {rounds.ToString(CultureInfo.InvariantCulture)} {(rounds == 1 ? "round" : "rounds")}";

    /// <summary>The condition's name from the pack when it has one, else the id with a capital: <c>"Lit"</c>.</summary>
    public static string Condition(ConditionId id, RulesPack rules)
        => rules.Conditions.TryGetValue(id, out var def) ? def.Name : Capitalise(id.Value);

    /// <summary>The text with its first letter in upper case.</summary>
    public static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>An id as words: the part after the last dot, underscores as spaces (<c>"beasts.dire_wolf"</c> → <c>"dire wolf"</c>).</summary>
    public static string Humanize(string id)
    {
        var dot = id.LastIndexOf('.');
        return (dot >= 0 ? id[(dot + 1)..] : id).Replace('_', ' ');
    }

    /// <summary><c>"a"</c> or <c>"an"</c> for the word.</summary>
    public static string Article(string word) => word.Length > 0 && "aeiouAEIOU".Contains(word[0]) ? "an" : "a";

    /// <summary>
    /// A dice table at <paramref name="level"/> plus named addends, joined with " + ": <c>"2d8 + WIS modifier"</c>. Empty when
    /// there is neither.
    /// </summary>
    public static string Amount(ByLevel<Dice>? dice, ImmutableArray<string> bonus, int level)
    {
        List<string> parts = [];
        if (dice is not null)
            parts.Add(dice.At(level).ToString());
        if (!bonus.IsDefaultOrEmpty)
            parts.AddRange(bonus.Select(Addend));
        return string.Join(" + ", parts);
    }

    private static string Addend(string name)
        => name switch
        {
            "prof" => "proficiency bonus",
            "level" => "level",
            "mod.cast" => "casting modifier",
            _ when name.StartsWith("mod.", StringComparison.Ordinal) => $"{name[4..].ToUpperInvariant()} modifier",
            _ => name,
        };

    /// <summary>An area's footprint in plain words: <c>"burst 2"</c>, <c>"cone 3"</c>, <c>"all allies"</c>, <c>"single tile"</c>.</summary>
    public static string Area(AreaDef area)
        => area.Shape switch
        {
            AreaShape.Single => "single tile",
            AreaShape.Self => "your tile",
            AreaShape.AllAllies => "all allies",
            AreaShape.AllEnemies => "all enemies",
            var shape => $"{shape.ToString().ToLowerInvariant()} {area.Size.ToString(CultureInfo.InvariantCulture)}",
        };

    /// <summary>
    /// The clauses of a modifier set, joined with "; ", worded for the unit that has them (<c>"+1 AC"</c>,
    /// <c>"disadvantage on its attack rolls"</c>). <c>"no modifiers"</c> when there are none.
    /// </summary>
    public static string Mods(StaticMods? mods, RulesPack rules)
        => Clauses(mods, rules) is { Count: > 0 } clauses ? string.Join("; ", clauses) : "no modifiers";

    /// <summary>The clauses of <see cref="Mods"/>, none for no modifiers.</summary>
    public static List<string> Clauses(StaticMods? mods, RulesPack rules)
    {
        List<string> clauses = [];
        if (mods is null)
            return clauses;
        if (mods.Ac != 0)
            clauses.Add($"{Signed(mods.Ac)} AC");
        if (mods.Attack != 0)
            clauses.Add($"{Signed(mods.Attack)} to attack rolls");
        if (mods.Damage != 0)
            clauses.Add($"{Signed(mods.Damage)} weapon damage");
        if (mods.Move != 0)
            clauses.Add($"{Signed(mods.Move)} Move");
        if (mods.Initiative != 0)
            clauses.Add($"{Signed(mods.Initiative)} Initiative");
        foreach (var (stat, bonus) in mods.Saves ?? ImmutableSortedDictionary<Stat, int>.Empty)
            if (bonus != 0)
                clauses.Add($"{Signed(bonus)} {stat} saves");
        if (mods.Incapacitated)
            clauses.Add("can't act");
        if (mods.CantMove)
            clauses.Add("can't move");
        if (mods.OwnAttacks != Edge.None)
            clauses.Add($"{EdgeWord(mods.OwnAttacks)} on its attack rolls");
        if (mods.AttackedMelee != Edge.None && mods.AttackedMelee == mods.AttackedRanged)
            clauses.Add($"attacks against it have {EdgeWord(mods.AttackedMelee)}");
        else
        {
            if (mods.AttackedMelee != Edge.None)
                clauses.Add($"melee attacks against it have {EdgeWord(mods.AttackedMelee)}");
            if (mods.AttackedRanged != Edge.None)
                clauses.Add($"ranged attacks against it have {EdgeWord(mods.AttackedRanged)}");
        }
        if (mods.AutoFailSaves is { Count: > 0 } fails)
            clauses.Add($"automatically fails {string.Join(" and ", fails)} saves");
        if (mods.CritWhenAdjacent)
            clauses.Add("hits from adjacent attackers are critical hits");
        if (mods.Invisible)
            clauses.Add("invisible");
        if (mods.Resist is { Count: > 0 } resist)
            clauses.Add($"resists {string.Join(", ", resist)}");
        if (mods.Immune is { Count: > 0 } immune)
            clauses.Add($"immune to {string.Join(", ", immune)}");
        if (mods.ExtraActions != 0)
            clauses.Add($"{Signed(mods.ExtraActions)} {(Math.Abs(mods.ExtraActions) == 1 ? "action" : "actions")} each turn");
        return clauses;
    }

    private static string Signed(int n) => n > 0 ? $"+{n}" : $"-{-n}";

    private static string EdgeWord(Edge edge) => edge == Edge.Advantage ? "advantage" : "disadvantage";

    /// <summary>
    /// A hook's test in words (<c>"you have advantage or you are flanking the target"</c>). Every key present must hold, so
    /// the parts are joined with "and"; a combinator inside another is wrapped in brackets.
    /// </summary>
    public static string When(WhenDef when, RulesPack rules) => When(when, rules, top: true);

    private static string When(WhenDef when, RulesPack rules, bool top)
    {
        List<(string Text, bool Compound)> parts = [];
        if (!when.All.IsDefaultOrEmpty)
            parts.Add((string.Join(" and ", when.All.Select(c => When(c, rules, top: false))), when.All.Length > 1));
        if (!when.Any.IsDefaultOrEmpty)
            parts.Add((string.Join(" or ", when.Any.Select(c => When(c, rules, top: false))), when.Any.Length > 1));
        if (when.Not is { } not)
            parts.Add(($"not {When(not, rules, top: false)}", false));
        if (!when.Weapon.IsDefaultOrEmpty)
            parts.Add(($"you attack with a {string.Join(" or ", when.Weapon)} weapon", false));
        if (when.Advantage is { } advantage)
            parts.Add((advantage ? "you have advantage" : "you don't have advantage", false));
        if (when.Flanked is { } flanked)
            parts.Add((flanked ? "you are flanking the target" : "you aren't flanking the target", false));
        if (when.TargetHasCondition is { } condition)
            parts.Add(($"the target is {Condition(condition, rules)}", false));
        if (when.HpBelow is { } hp)
            parts.Add(($"the target is below {hp}% HP", false));
        if (when.HeightAbove is { } height)
            parts.Add(($"you are at least {height} {(height == 1 ? "level" : "levels")} above the target", false));
        if (when.IsSpell is { } spell)
            parts.Add((spell ? "it is a spell" : "it isn't a spell", false));
        if (when.DamageType is { } type)
            parts.Add(($"the damage is {type}", false));
        if (when.TargetTag is { } tag)
            parts.Add(($"the target is {tag}", false));
        if (when.Success is { } success)
            parts.Add((success ? "the roll succeeds" : "the roll fails", false));

        if (parts.Count == 0)
            return "always";
        if (parts.Count == 1)
            return top || !parts[0].Compound ? parts[0].Text : $"({parts[0].Text})";
        var text = string.Join(" and ", parts.Select(p => p.Compound ? $"({p.Text})" : p.Text));
        return top ? text : $"({text})";
    }
}
