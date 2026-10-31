using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The rule for a character's picks (spec §4.5 "choose N of" groups), shared by <see cref="SheetBuilder"/> (a character's
/// picks) and the loader (a class's <c>defaultPicks</c>): each pick must be offered by a <c>choose</c> feature at or below
/// the level, and each <c>choose</c> feature takes at most its own count, so two features' counts never pool (picks are
/// matched to features with augmenting paths, so a pick that several features offer goes wherever it fits).
/// </summary>
internal static class PickRules
{
    /// <summary>
    /// The problems with <paramref name="picks"/> for a <paramref name="level"/> <paramref name="cls"/>, offered by
    /// <paramref name="choices"/> (each <c>choose</c> feature with the level it comes at), as JSON pointers relative to the
    /// pick list (<c>""</c> for the list, <c>"/&lt;i&gt;"</c> for one pick) with their messages. Picks
    /// <paramref name="known"/> rejects are skipped (their caller reports them); repeated picks count once.
    /// </summary>
    public static List<(string Pointer, string Message)> Check(IReadOnlyList<AbilityId> picks,
        IReadOnlyList<(int Level, FeatureRef Feature)> choices, string cls, int level, Func<AbilityId, bool> known)
    {
        var problems = new List<(string, string)>();
        var offered = choices.SelectMany(c => c.Feature.From).ToHashSet(StringComparer.Ordinal);
        var allowed = choices.Sum(c => c.Feature.Choose ?? 0);

        var counted = new List<(int Index, AbilityId Pick)>();
        var seen = new HashSet<AbilityId>();
        for (var i = 0; i < picks.Count; i++)
        {
            if (!known(picks[i]))
                continue;
            if (!offered.Contains(picks[i].Value))
                problems.Add(($"/{i}", $"'{picks[i]}' isn't one of the abilities this class offers a choice of at level {level}."));
            else if (seen.Add(picks[i]))
                counted.Add((i, picks[i]));
        }
        if (counted.Count > allowed)
        {
            problems.Add(("", $"A level {level} {cls} chooses {allowed} abilit{(allowed == 1 ? "y" : "ies")}, not {counted.Count}."));
            return problems;
        }

        // Match picks to features (a feature with count n is n slots); a pick with no slot left is one too many.
        var slotOwner = new int?[choices.Count][];
        for (var f = 0; f < choices.Count; f++)
            slotOwner[f] = new int?[choices[f].Feature.Choose ?? 0];
        for (var p = 0; p < counted.Count; p++)
        {
            if (Place(p, counted, choices, slotOwner, new bool[choices.Count]))
                continue;
            var first = choices.First(c => c.Feature.From.Contains(counted[p].Pick.Value));
            problems.Add(($"/{counted[p].Index}", $"'{counted[p].Pick}' is one pick too many: the level {first.Level} choice takes "
                                                  + $"{first.Feature.Choose} of {string.Join(", ", first.Feature.From)}."));
        }
        return problems;
    }

    /// <summary>Finds a slot for pick <paramref name="p"/>, moving earlier picks along augmenting paths when needed.</summary>
    private static bool Place(int p, List<(int Index, AbilityId Pick)> picks, IReadOnlyList<(int Level, FeatureRef Feature)> choices,
        int?[][] slotOwner, bool[] visited)
    {
        for (var f = 0; f < choices.Count; f++)
        {
            if (visited[f] || !choices[f].Feature.From.Contains(picks[p].Pick.Value))
                continue;
            visited[f] = true;
            var slots = slotOwner[f];
            for (var s = 0; s < slots.Length; s++)
                if (slots[s] is null)
                {
                    slots[s] = p;
                    return true;
                }
            for (var s = 0; s < slots.Length; s++)
                if (Place(slots[s]!.Value, picks, choices, slotOwner, visited))
                {
                    slots[s] = p;
                    return true;
                }
        }
        return false;
    }

    /// <summary>The <c>choose</c> features of a class (and a subclass, if given) from level 1 to <paramref name="level"/>.</summary>
    public static ImmutableArray<(int Level, FeatureRef Feature)> Choices(ClassDef cls, SubclassDef? subclass, int level)
        => [.. Enumerable.Range(1, level)
            .SelectMany(l => Features(cls.Features, l).Concat(subclass is null ? [] : Features(subclass.Features, l)).Select(f => (l, f)))
            .Where(c => c.f.Choose is not null)];

    private static IEnumerable<FeatureRef> Features(ImmutableSortedDictionary<int, ImmutableArray<FeatureRef>> features, int level)
        => features.TryGetValue(level, out var list) ? list : [];
}
