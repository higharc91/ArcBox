using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// What a player may do to their character between sessions (sub-project 4, rules-engine spec §3.1), on top of what
/// <see cref="SheetBuilder.ForCharacter"/> already checks (ids, levels, picks against the "choose N" features):
/// <list type="bullet">
/// <item><b>Point-buy:</b> base scores 8–15, costing 0, 1, 2, 3, 4, 5, 7 and 9 points, at most 27 in all.</item>
/// <item><b>Ability score increases:</b> 2 points at level 4 and 2 more at 8, spent as +2 to one score or +1 to two. A score
/// stays at most 20 with its background boost. Feats come with content that defines them.</item>
/// <item><b>Subclass:</b> chosen from level 3, and kept from then on.</item>
/// </list>
/// Each check returns the player-facing problem, or null when the change is allowed.
/// </summary>
public static class CharacterRules
{
    /// <summary>The point-buy budget.</summary>
    public const int PointBuy = 27;

    /// <summary>The level a subclass is chosen at.</summary>
    public const int SubclassLevel = 3;

    private static readonly int[] Cost = [0, 1, 2, 3, 4, 5, 7, 9];   // for scores 8..15

    /// <summary>Points a set of base scores costs; null if a score is outside 8–15 or a stat is missing.</summary>
    public static int? PointsSpent(IReadOnlyDictionary<Stat, int> scores)
    {
        var total = 0;
        foreach (var stat in Enum.GetValues<Stat>())
        {
            if (!scores.TryGetValue(stat, out var score) || score is < 8 or > 15)
                return null;
            total += Cost[score - 8];
        }
        return total;
    }

    /// <summary>Increase points a character of <paramref name="level"/> has earned: 2 at level 4, 4 from level 8.</summary>
    public static int IncreasesDue(int level) => level >= 8 ? 4 : level >= 4 ? 2 : 0;

    /// <summary>Increase points earned and not yet spent.</summary>
    public static int IncreasesLeft(CharacterRecord c) => IncreasesDue(c.Level) - c.Increases.Values.Sum();

    /// <summary>True when the character has reached its subclass level and hasn't chosen one.</summary>
    public static bool NeedsSubclass(CharacterRecord c) => c.Level >= SubclassLevel && c.Subclass is null;

    /// <summary>
    /// A new character: <paramref name="race"/>, <paramref name="cls"/> and <paramref name="background"/> from the pack,
    /// point-bought <paramref name="scores"/>, at <paramref name="level"/> with the class's starting kit and default picks
    /// (changeable later). Returns the problem instead when something isn't allowed.
    /// </summary>
    public static (CharacterRecord? Character, string? Problem) Create(string name, string race, string cls, string background,
        IReadOnlyDictionary<Stat, int> scores, int level, RulesPack rules)
    {
        if (!rules.Races.ContainsKey(race))
            return (null, "Pick one of the races.");
        if (!rules.Classes.TryGetValue(cls, out var def))
            return (null, "Pick one of the classes.");
        if (!rules.Backgrounds.ContainsKey(background))
            return (null, "Pick one of the backgrounds.");
        switch (PointsSpent(scores))
        {
            case null:
                return (null, "Every ability score starts between 8 and 15.");
            case > PointBuy and var spent:
                return (null, $"That's {spent} points; you have {PointBuy}.");
        }
        var character = new CharacterRecord(name, race, cls, null, background, level, Ordinal(scores),
            ImmutableSortedDictionary<Stat, int>.Empty, SheetBuilder.DefaultLoadout(def, level), [], def.StartingKit.Weapon,
            def.StartingKit.Armor, def.StartingKit.Shield, def.StartingKit.Bag);
        return Built(character, rules);
    }

    /// <summary>
    /// Sets the character's picks (spells and options known): any set the class's "choose" features allow. An empty set is
    /// refused: a stored empty list means "the class's default loadout", which isn't what the player chose.
    /// </summary>
    public static (CharacterRecord? Character, string? Problem) SetPicks(CharacterRecord c, IReadOnlyList<AbilityId> picks, RulesPack rules)
        => picks.Count == 0 ? (null, "Choose at least one.") : Built(c with { Picks = [.. picks.Distinct()] }, rules);

    /// <summary>Chooses the subclass, once, from level 3.</summary>
    public static (CharacterRecord? Character, string? Problem) ChooseSubclass(CharacterRecord c, string subclass, RulesPack rules)
    {
        if (c.Level < SubclassLevel)
            return (null, $"You choose a subclass at level {SubclassLevel}.");
        if (c.Subclass is not null)
            return (null, "Your subclass is already chosen.");
        if (!rules.Classes.TryGetValue(c.Class, out var cls) || cls.Subclasses.All(s => s.Id != subclass))
            return (null, "Pick one of your class's subclasses.");
        return Built(c with { Subclass = subclass }, rules);
    }

    /// <summary>Spends increase points: +2 to one score, or +1 to each of two.</summary>
    public static (CharacterRecord? Character, string? Problem) SpendIncrease(CharacterRecord c, IReadOnlyList<Stat> stats, RulesPack rules)
    {
        if (IncreasesLeft(c) < 2)
            return (null, "You have no ability score increase to spend.");
        if (stats.Count is < 1 or > 2)
            return (null, "Raise one score by 2, or two scores by 1.");
        var add = stats.Count == 1 ? new Dictionary<Stat, int> { [stats[0]] = 2 }
            : stats[0] == stats[1] ? new Dictionary<Stat, int> { [stats[0]] = 2 }
            : new Dictionary<Stat, int> { [stats[0]] = 1, [stats[1]] = 1 };
        var boosts = rules.Backgrounds.GetValueOrDefault(c.Background)?.Boosts ?? ImmutableSortedDictionary<Stat, int>.Empty;
        foreach (var (stat, n) in add)
        {
            var now = c.BaseScores.GetValueOrDefault(stat, 10) + boosts.GetValueOrDefault(stat) + c.Increases.GetValueOrDefault(stat);
            if (now + n > 20)
                return (null, $"{stat.ToString().ToUpperInvariant()} can't go above 20.");
        }
        var increases = c.Increases;
        foreach (var (stat, n) in add)
            increases = increases.SetItem(stat, increases.GetValueOrDefault(stat) + n);
        return Built(c with { Increases = increases }, rules);
    }

    /// <summary>
    /// The character at a higher <paramref name="level"/>, kept as the player made it. Its picks gain the class's default
    /// choices for the new slots (so the character is whole in the next battle); the player can change them, choose the
    /// subclass and spend increases in the portal ("banked" choices, spec §3.4).
    /// </summary>
    public static CharacterRecord LevelUp(CharacterRecord c, int level, RulesPack rules)
    {
        if (level <= c.Level || !rules.Classes.TryGetValue(c.Class, out var cls))
            return c with { Level = Math.Max(level, c.Level) };
        var up = c with { Level = level };
        if (c.Picks.IsDefaultOrEmpty)
            return up;   // no picks of its own: the default loadout follows the level
        // Each new default joins if it still fits; one that doesn't (the player's choices took its slot) leaves the slot open.
        foreach (var pick in SheetBuilder.DefaultLoadout(cls, level).Except(SheetBuilder.DefaultLoadout(cls, c.Level)).Except(c.Picks))
            if (Built(up with { Picks = up.Picks.Add(pick) }, rules).Character is { } grown)
                up = grown;
        return up;
    }

    /// <summary>The character if its sheet builds, else the first problem, as the player sees it.</summary>
    private static (CharacterRecord? Character, string? Problem) Built(CharacterRecord c, RulesPack rules)
    {
        try
        {
            SheetBuilder.ForCharacter(c, rules);
            return (c, null);
        }
        catch (ContentException e)
        {
            return (null, e.Errors.FirstOrDefault()?.Message ?? "That character isn't valid.");
        }
    }

    private static ImmutableSortedDictionary<Stat, int> Ordinal(IReadOnlyDictionary<Stat, int> scores)
        => ImmutableSortedDictionary.CreateRange(scores);
}
