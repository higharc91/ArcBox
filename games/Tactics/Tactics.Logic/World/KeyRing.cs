using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A key the party holds, on the key ring (interactables design §1.2, USER DECISION 8, decided by the owner on 2026-11-03): the flag its locks
/// name (<see cref="Id"/>), what it's called, where it was found (null when the story gave it), and whether it has opened a lock yet.
/// </summary>
public sealed record KeyInfo(string Id, string Name, string? Found, bool Used);

/// <summary>
/// The party's key ring: keys stay story flags, as they always were (an item would fail the sheet builder), and this reads them back as keys.
/// A key is a flag some lock in the pack names (<see cref="LockDef.Key"/>) that the party has set; it's called by the lock's
/// <see cref="LockDef.KeyName"/>, else by the thing that gave it. Pure: the caller passes the saves as they are now.
/// </summary>
public static class KeyRing
{
    /// <summary>
    /// The keys of <paramref name="rules"/> the party holds with <paramref name="flags"/> (in the order the flags were set, so the newest is
    /// last), and for each where it was found: the thing whose <c>sets</c> gave it, as "Ashen Ford" for a key picked up there, or "the reeve's
    /// desk, Ashen Ford" for one found in something. Used once any lock that takes it was opened with it (<see cref="InteractableSave.Keyed"/>).
    /// </summary>
    public static ImmutableArray<KeyInfo> Of(RulesPack rules, IReadOnlyList<string> flags, IReadOnlyDictionary<string, InteractableSave>? saves)
    {
        var locks = rules.Interactables.Values.SelectMany(r => r).Where(d => d.Lock?.Key is not null).ToList();
        if (locks.Count == 0)
            return [];
        var keys = new List<KeyInfo>();
        foreach (var flag in flags.Distinct(StringComparer.Ordinal))
        {
            var opens = locks.Where(d => d.Lock!.Key == flag).ToList();
            if (opens.Count == 0)
                continue;
            // Only a thing the party has used (taken, opened, read…): a story that set the flag names no place, and never an unfound one.
            var (source, region) = rules.Interactables
                .SelectMany(r => r.Value.Select(d => (Def: d, Region: r.Key)))
                .FirstOrDefault(t => t.Def.Sets.Contains(flag) && Done(t.Def, saves));
            var name = opens.Select(d => d.Lock!.KeyName).FirstOrDefault(n => n is not null)
                ?? (source is not null && Interactables.KindOf(source).Family == InteractableFamily.Pickup ? Interactables.NameOf(source) : null)
                ?? "key";
            string? found = null;
            if (source is not null)
            {
                var place = rules.RegionFor(region)?.Def.Name ?? region;
                found = Interactables.KindOf(source).Family == InteractableFamily.Pickup ? place : $"the {Interactables.NameOf(source)}, {place}";
            }
            var used = opens.Any(d => saves is not null && saves.TryGetValue(d.Id, out var save) && save.Keyed);
            keys.Add(new KeyInfo(flag, name, found, used));
        }
        return [.. keys];
    }

    /// <summary>True when the party has done the thing's main outcome: it's no longer in its first state, or (a notice, a path) it was found.</summary>
    private static bool Done(InteractableDef def, IReadOnlyDictionary<string, InteractableSave>? saves)
        => saves is not null && saves.TryGetValue(def.Id, out var save)
           && (save.State != Interactables.Initial(def).State || (Interactables.FoundIsItsOutcome(def) && save.Found));
}
