using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Stable people identities and the same flag gates as world interactions.</summary>
public static class RegionPeople
{
    public static string Id(RegionPerson person, int index) => person.Id ?? $"person:{index}:{person.Preset}";

    public static bool Present(RegionPerson person, IReadOnlySet<string> flags)
        => (person.Requires.IsDefaultOrEmpty || person.Requires.All(flags.Contains))
            && (person.Unless.IsDefaultOrEmpty || !person.Unless.Any(flags.Contains));

    /// <summary>
    /// The existing ford choices commit their outcome flags before the fight. People leave only once the corresponding battle
    /// actually succeeded: a loss must not remove the living chief or the clan. Saved story flags are never changed here.
    /// </summary>
    public static ImmutableHashSet<string> PresenceFlags(IEnumerable<string> flags, IReadOnlySet<string> completedBeats)
    {
        var settled = flags.ToImmutableHashSet(StringComparer.Ordinal);
        if (!completedBeats.Contains("ch1_ford_assault")) settled = settled.Remove("ch1_skarrow_slain");
        if (!completedBeats.Contains("ch1_river_crossing")) settled = settled.Remove("ch1_skarrow_spared");
        return settled;
    }
}
