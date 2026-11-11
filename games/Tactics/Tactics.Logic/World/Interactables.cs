using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The rules of things to interact with in the world (interactables design §2), with no clock and no dice: what's there, what can be seen,
/// what stands in the way, what's in the leader's reach and what the button says. <see cref="InteractScenes"/> says what interacting does.
/// </summary>
public static class Interactables
{
    /// <summary>A lock opened with its key.</summary>
    public const string KeyApproach = "key";

    /// <summary>A lock picked: Sleight of Hand.</summary>
    public const string PickApproach = "pick";

    /// <summary>A lock forced: Athletics.</summary>
    public const string ForceApproach = "force";

    /// <summary>A magic seal broken: Arcana.</summary>
    public const string SealApproach = "seal";

    /// <summary>A trap taken apart: Sleight of Hand.</summary>
    public const string DisarmApproach = "disarm";

    /// <summary>How far from the leader, in tiles along x and y, the party's passive Perception finds hidden things and traps (design §2.6).</summary>
    public const int RevealRange = 3;

    /// <summary>The farthest a thing's own <see cref="InteractableDef.Range"/> may reach (design §7.2).</summary>
    public const int MaxRange = 6;

    /// <summary>How far from the leader, in tiles along x and y, an active Search looks (design §7.4).</summary>
    public const int SearchRange = 2;

    /// <summary>The skill an active Search rolls (design §7.4).</summary>
    public const string SearchSkill = "investigation";

    /// <summary>How far the party's passive skills reach for <paramref name="def"/>: its own range, else <see cref="RevealRange"/>.</summary>
    public static int RangeOf(InteractableDef def) => def.Range ?? RevealRange;

    /// <summary>True for a kind whose finding is its whole outcome (a notice, a hidden path): its <c>sets</c> are set as it's found.</summary>
    public static bool FoundIsItsOutcome(InteractableDef def) => KindOf(def).Family is InteractableFamily.Notice or InteractableFamily.Path;

    /// <summary>The highest passive Perception DC content may set, and the highest check DC: the SRD's "nearly impossible".</summary>
    public const int MaxDc = 30;

    /// <summary>The kind of <paramref name="def"/>. Throws for an unknown kind, which the loader refuses.</summary>
    public static InteractableKind KindOf(InteractableDef def)
        => InteractableKinds.Of(def.Kind) ?? throw new ArgumentException($"Unknown interactable kind '{def.Kind}'.", nameof(def));

    /// <summary>Its state before anyone has touched it.</summary>
    public static InteractableSave Initial(InteractableDef def)
    {
        var kind = KindOf(def);
        var state = kind.Family switch
        {
            InteractableFamily.Passage when def.Kind == "cave_mouth" => InteractableKinds.Idle,
            InteractableFamily.Passage => def.Open ? InteractableKinds.Opened : InteractableKinds.Closed,
            InteractableFamily.Switch => def.Open ? InteractableKinds.On : InteractableKinds.Off,
            _ => kind.States[0],
        };
        return new InteractableSave(state);
    }

    /// <summary>Its state as <paramref name="saves"/> keep it, or its <see cref="Initial"/> one. A state the kind doesn't have (an older pack's) reads as the initial.</summary>
    public static InteractableSave StateOf(InteractableDef def, IReadOnlyDictionary<string, InteractableSave>? saves)
        => saves is not null && saves.TryGetValue(def.Id, out var save) && KindOf(def).States.Contains(save.State) ? save : Initial(def);

    /// <summary>True when it's there with these flags: every one of its <c>requires</c> is set and none of its <c>unless</c>.</summary>
    public static bool Present(InteractableDef def, IReadOnlySet<string> flags)
        => def.Requires.All(flags.Contains) && !def.Unless.Any(flags.Contains);

    /// <summary>
    /// True when the TV may draw it and the leader may use it: it's there, it isn't hidden or has been found, and a floor trap only once it's
    /// been seen or has gone off. Nothing else is ever sent (design §2.9).
    /// </summary>
    public static bool Visible(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
    {
        if (!Present(def, flags) || (def.Hidden is not null && !save.Found))
            return false;
        return KindOf(def).Family != InteractableFamily.FloorTrap || save.TrapSpotted || save.Trap is not null;
    }

    /// <summary>
    /// True when it stands in the way of walking (design §2.4): a passage while it's closed (found or not: a hidden door is a wall, which the
    /// TV draws as wall, <see cref="Plugged"/>), a floor trap that's been seen and is still set (the party walks round it), and otherwise as
    /// its kind or content says, once it's been found if it's hidden: a hidden chest nobody has found is walked through, or the party would
    /// stop at nothing and give it away. Something that isn't there blocks nothing.
    /// </summary>
    public static bool Blocks(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
    {
        if (!Present(def, flags))
            return false;
        var kind = KindOf(def);
        if (kind.Family == InteractableFamily.Path)
            return !save.Found;         // a hidden path is in the way until it's found, and then it's ground (design §7.3)
        if (def.Hidden is not null && !save.Found && kind.Family != InteractableFamily.Passage)
            return false;
        return kind.Family switch
        {
            InteractableFamily.Passage => save.State == InteractableKinds.Closed,
            InteractableFamily.FloorTrap => save.TrapSpotted && save.Trap is null,
            _ => def.Blocks ?? kind.Blocks,
        };
    }

    /// <summary>
    /// True for a shut passage nobody has found (a secret door), or a hidden path nobody has found (design §7.3): it blocks, but the TV isn't
    /// told it's there, so the TV is sent a plug for its tile instead (<c>ExploreView.Plugs</c>), drawn as its neighbours along its facing's
    /// run (the wall, the hedge, the deep water it hides in), or its gap would give it away.
    /// </summary>
    public static bool Plugged(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
        => KindOf(def).Family is InteractableFamily.Passage or InteractableFamily.Path && Blocks(def, save, flags) && !Visible(def, save, flags);

    /// <summary>
    /// True for a thing that may stand in the way whatever has happened (a chest, a barrel): neither a passage, whose state opens it, nor a trap
    /// on the ground, which only blocks once seen. The content checks and the story-order walk keep these in the way, with every passage open.
    /// </summary>
    public static bool Solid(InteractableDef def)
        => KindOf(def).Family is not (InteractableFamily.Passage or InteractableFamily.FloorTrap) && (def.Blocks ?? KindOf(def).Blocks);

    /// <summary>The tiles the things of a region block now, in tile order.</summary>
    public static ImmutableSortedSet<Tile> Blocking(IEnumerable<InteractableDef> defs, IReadOnlyDictionary<string, InteractableSave>? saves,
        IReadOnlySet<string> flags)
        => [.. defs.Where(d => Blocks(d, StateOf(d, saves), flags)).Select(d => d.At)];

    /// <summary>
    /// True when the leader at <paramref name="at"/> can reach the thing: on its tile or one of the eight round it, and no more than a
    /// climb (<see cref="ClassDef.DefaultJump"/>) above or below it, so a chest up a cliff isn't in reach from its foot. Diagonally only as
    /// walking goes diagonally (<see cref="WorldWalk"/>): both corner tiles walkable, so nobody reaches round the corner of a wall.
    /// </summary>
    public static bool InReach(Terrain terrain, Tile at, InteractableDef def)
    {
        var (dx, dy) = (def.At.X - at.X, def.At.Y - at.Y);
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > 1 || !terrain.Map.InBounds(at) || !terrain.Map.InBounds(def.At)
            || Math.Abs(terrain.HeightAt(at) - terrain.HeightAt(def.At)) > ClassDef.DefaultJump)
            return false;
        return dx == 0 || dy == 0 || (terrain.Walkable(new Tile(at.X + dx, at.Y)) && terrain.Walkable(new Tile(at.X, at.Y + dy)));
    }

    /// <summary>The ids of the passages some lever of <paramref name="defs"/> works: they open and shut only by it.</summary>
    public static ImmutableHashSet<string> Worked(IEnumerable<InteractableDef> defs)
        => [.. defs.Where(d => KindOf(d).Family == InteractableFamily.Switch).SelectMany(d => d.Targets)];

    /// <summary>What it's called: its content's name, else its kind's.</summary>
    public static string NameOf(InteractableDef def) => def.Name ?? KindOf(def).Name;

    /// <summary>Its name with a capital, as a title.</summary>
    public static string TitleOf(InteractableDef def) => Capital(NameOf(def));

    internal static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>What it says: its content's words, else its kind's.</summary>
    public static ImmutableArray<string> TextOf(InteractableDef def) => def.Text.IsDefaultOrEmpty ? KindOf(def).Text : def.Text;

    /// <summary>Its art key: its content's, else its kind's.</summary>
    public static string ArtOf(InteractableDef def) => def.Art ?? KindOf(def).Art;

    /// <summary>
    /// The state its art is drawn in: the kind's state, except a floor trap's, which is its trap's (<c>armed</c>, <c>disarmed</c> or
    /// <c>sprung</c>).
    /// </summary>
    public static string ArtState(InteractableDef def, InteractableSave save)
        => KindOf(def).Family == InteractableFamily.FloorTrap ? save.Trap ?? "armed" : save.State;

    /// <summary>
    /// The art the pack has now for it in its art state (design §4.3), or null for a plain marker: only what the kind names for that very
    /// state, never another state's, so an open chest is never drawn shut. Content's own art key wins.
    /// </summary>
    public static string? StandInOf(InteractableDef def, InteractableSave save)
        => def.Art ?? KindOf(def).StandIn.GetValueOrDefault(ArtState(def, save));

    /// <summary>
    /// True when its art state draws something: a pickup that's been taken and a cache that's been emptied draw nothing
    /// (<see cref="InteractableKind.Empty"/>), so the TV isn't sent them.
    /// </summary>
    public static bool Drawn(InteractableDef def, InteractableSave save) => !KindOf(def).Empty.Contains(ArtState(def, save));

    /// <summary>True for a container or passage whose trap is set and has been seen: the TV shows it's trapped.</summary>
    public static bool Trapped(InteractableDef def, InteractableSave save) => def.Trap is not null && save.TrapSpotted && save.Trap is null;

    /// <summary>
    /// The leader's button for it (design §2.5), or null when there's nothing to do: an open container or passage, a used shrine, a pickup
    /// taken, a trap gone. A lever, a readable and an ambient thing always have one. A passage a lever works says what it is, as touching it
    /// only says it won't budge. A thing that isn't <see cref="Visible"/> has none.
    /// </summary>
    public static string? Label(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags, bool worked)
    {
        if (!Visible(def, save, flags))
            return null;
        var name = NameOf(def);
        var kind = KindOf(def);
        if (def.To is not null) return $"Enter {name}";
        return kind.Family switch
        {
            InteractableFamily.Container when save.State == InteractableKinds.Closed => $"Open the {name}",
            InteractableFamily.Passage when save.State == InteractableKinds.Closed => worked ? $"Look at the {name}" : $"Open the {name}",
            InteractableFamily.Rest => "Make camp",
            InteractableFamily.Switch => $"Pull the {name}",
            InteractableFamily.Readable => $"Read the {name}",
            InteractableFamily.Pickup when save.State == InteractableKinds.Here => $"Pick up the {name}",
            InteractableFamily.Shrine when save.State == InteractableKinds.Ready => kind.Id == "well" ? $"Draw from the {name}" : $"Kneel at the {name}",
            InteractableFamily.Ambient => $"Look at the {name}",
            InteractableFamily.FloorTrap when save.TrapSpotted && save.Trap is null => $"Disarm the {name}",
            InteractableFamily.Notice => kind.Id == "draft" ? $"Feel the {name}" : $"Look at the {name}",
            _ => null,
        };
    }

    /// <summary>
    /// What the leader at <paramref name="at"/>, facing <paramref name="facing"/>, can use now, with each one's button: the nearest first,
    /// then the one on the faced tile, then by id. Nearest before faced, so the list doesn't reorder with every turn of the walk (the walk
    /// screen must not change under a thumb), but the thing in front of the leader still wins between two as near.
    /// </summary>
    public static ImmutableArray<(InteractableDef Def, string Label)> Targets(IEnumerable<InteractableDef> defs,
        IReadOnlyDictionary<string, InteractableSave>? saves, IReadOnlySet<string> flags, Terrain terrain, Tile at, Tile facing)
    {
        var all = defs.ToList();
        var worked = Worked(all);
        return [.. all
            .Select(d => (Def: d, Label: InReach(terrain, at, d) ? Label(d, StateOf(d, saves), flags, worked.Contains(d.Id)) : null))
            .Where(t => t.Label is not null)
            .OrderBy(t => Math.Abs(t.Def.At.X - at.X) + Math.Abs(t.Def.At.Y - at.Y))
            .ThenBy(t => t.Def.At == facing ? 0 : 1)
            .ThenBy(t => t.Def.Id, StringComparer.Ordinal)
            .Select(t => (t.Def, t.Label!))];
    }

    /// <summary>A character's passive Perception (SRD): 10 plus their Perception bonus (<see cref="SkillCheck.Bonus(UnitSheet, string)"/>).</summary>
    public static int Passive(UnitSheet sheet) => Passive(sheet, HiddenDef.Perception);

    /// <summary>A character's passive <paramref name="skill"/> (SRD): 10 plus their bonus in it (design §7.1).</summary>
    public static int Passive(UnitSheet sheet, string skill) => 10 + SkillCheck.Bonus(sheet, skill);

    /// <summary>What a passive Perception of <paramref name="passive"/> finds from <paramref name="at"/>.</summary>
    public enum Finding
    {
        /// <summary>A hidden thing is found.</summary>
        Revealed,
        /// <summary>A trap is seen.</summary>
        Spotted,
        /// <summary>A notice is noticed: its words are said, and what it reveals is found with it.</summary>
        Noticed,
    }

    /// <summary>What finding <paramref name="def"/> is: a notice is noticed, anything else revealed.</summary>
    public static Finding FindingOf(InteractableDef def) => KindOf(def).Family == InteractableFamily.Notice ? Finding.Noticed : Finding.Revealed;

    /// <summary>
    /// The hidden things found with <paramref name="notice"/> (design §7.2): those of <paramref name="defs"/> it names in its <c>reveals</c>, that
    /// are there with these flags and not found yet, and theirs in turn if they're notices, in that order, each once. Range and skill don't
    /// matter: the tracks lead there. A thing that isn't there isn't found, and stays hidden for when it is.
    /// </summary>
    public static ImmutableArray<(InteractableDef Def, Finding Found)> Revealing(InteractableDef notice, IEnumerable<InteractableDef> defs,
        IReadOnlyDictionary<string, InteractableSave>? saves, IReadOnlySet<string> flags)
    {
        var byId = defs.GroupBy(d => d.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { notice.Id };
        var found = ImmutableArray.CreateBuilder<(InteractableDef, Finding)>();
        var open = new Queue<InteractableDef>([notice]);
        while (open.TryDequeue(out var from))
            foreach (var id in from.Reveals.IsDefault ? [] : from.Reveals)
            {
                if (!seen.Add(id) || !byId.TryGetValue(id, out var def) || def.Hidden is null || !Present(def, flags) || StateOf(def, saves).Found)
                    continue;
                found.Add((def, FindingOf(def)));
                open.Enqueue(def);
            }
        return found.ToImmutable();
    }

    /// <summary>What the party's best passive Perception <paramref name="passive"/> finds: <see cref="Finds(IEnumerable{InteractableDef}, IReadOnlyDictionary{string, InteractableSave}?, IReadOnlySet{string}, Tile, Func{string, int})"/> with every skill at it.</summary>
    public static ImmutableArray<(InteractableDef Def, Finding Found)> Finds(IEnumerable<InteractableDef> defs,
        IReadOnlyDictionary<string, InteractableSave>? saves, IReadOnlySet<string> flags, Tile at, int passive)
        => [.. Finds(defs, saves, flags, at, _ => passive).Select(f => (f.Def, f.Found))];

    /// <summary>
    /// What the party's best passive skills (<paramref name="passive"/>, by skill) find with the leader at <paramref name="at"/> (design §2.6,
    /// §7.1): every hidden thing there and not yet found whose skill's passive meets its DC, and every trap still set and not yet seen whose
    /// spot DC passive Perception meets, each within its <see cref="RangeOf"/> tiles along x and y. A trap on a hidden thing is seen only once
    /// the thing is found, which can be in the same pass. In id order, each with the skill that found it. What a notice reveals isn't here:
    /// <see cref="Revealing"/> says, as it's applied.
    /// </summary>
    public static ImmutableArray<(InteractableDef Def, Finding Found, string Skill)> Finds(IEnumerable<InteractableDef> defs,
        IReadOnlyDictionary<string, InteractableSave>? saves, IReadOnlySet<string> flags, Tile at, Func<string, int> passive)
    {
        var found = new List<(InteractableDef, Finding, string)>();
        foreach (var def in defs.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (!Present(def, flags) || Math.Max(Math.Abs(at.X - def.At.X), Math.Abs(at.Y - def.At.Y)) > RangeOf(def))
                continue;
            var save = StateOf(def, saves);
            var seen = def.Hidden is null || save.Found;
            if (!seen && !def.SearchOnly && passive(def.Hidden!.Skill) >= def.Hidden.Dc)
            {
                found.Add((def, FindingOf(def), def.Hidden.Skill));
                seen = true;
            }
            if (seen && def.Trap is { } trap && save.Trap is null && !save.TrapSpotted && passive(HiddenDef.Perception) >= trap.Spot)
                found.Add((def, Finding.Spotted, HiddenDef.Perception));
        }
        return [.. found];
    }

    /// <summary>
    /// What an active Search from <paramref name="at"/> looks for (design §7.4): every hidden thing there, not found, not already searched
    /// for since the last long rest (the caller passes saves <see cref="Rested"/>), within <see cref="SearchRange"/> tiles along x and y, and not on the leader's own tile if revealing it would block them. Active-only nonblocking finds can be searched underfoot. In
    /// id order. The scene doesn't say how many: it plays the same with none.
    /// </summary>
    public static ImmutableArray<InteractableDef> Searchable(IEnumerable<InteractableDef> defs, IReadOnlyDictionary<string, InteractableSave>? saves,
        IReadOnlySet<string> flags, Tile at)
        => [.. defs.Where(d => d.Hidden is not null && (d.At != at || (d.SearchOnly && !Blocks(d, StateOf(d, saves) with { Found = true }, flags))) && Present(d, flags)
                && Math.Max(Math.Abs(at.X - d.At.X), Math.Abs(at.Y - d.At.Y)) <= SearchRange
                && StateOf(d, saves) is { Found: false, Searched: false })
            .OrderBy(d => d.Id, StringComparer.Ordinal)];

    /// <summary>True for a way through a lock that a long rest reopens once it has failed: pick, force and seal (USER DECISION 2). A disarm isn't: failing it sprang the trap.</summary>
    public static bool Reopens(string approach) => approach is PickApproach or ForceApproach or SealApproach;

    /// <summary>True when <paramref name="save"/> has a failed way or search that a long rest reopens.</summary>
    public static bool HasReopenable(InteractableSave save) => save.Searched || save.Spent.Any(Reopens);

    /// <summary>
    /// <paramref name="save"/> as it is once the party's long rests number <paramref name="restSeq"/> (USER DECISION 2, decided 2026-11-03):
    /// a failed pick, force or seal, and a failed search, are spent until the rest <see cref="InteractableSave.Rest"/> names, the one after
    /// they failed, so once it has come they're there to try again, and the save is kept as of the next rest (<c>restSeq + 1</c>), so its
    /// rest only goes up. A save that names no rest (0, written before this) stays as it is: the document stamps those as it's read
    /// (<see cref="Stamped"/>). A failed disarm stays spent (its trap went off). Anything else is as it was.
    /// </summary>
    public static InteractableSave Rested(InteractableSave save, int restSeq)
        => save.Rest != 0 && save.Rest <= restSeq && HasReopenable(save)
            ? save with { Spent = [.. save.Spent.Where(a => !Reopens(a))], Searched = false, Rest = restSeq + 1 }
            : save;

    /// <summary>
    /// <paramref name="save"/> with its failed ways and search spent until the next long rest after <paramref name="restSeq"/>, if they name
    /// no rest yet: a save from before USER DECISION 2 was built (<see cref="InteractableSave.Rest"/> 0), or one failing now.
    /// </summary>
    public static InteractableSave Stamped(InteractableSave save, int restSeq)
        => save.Rest == 0 && HasReopenable(save) ? save with { Rest = restSeq + 1 } : save;

    /// <summary>
    /// True when <paramref name="after"/> has gone back on <paramref name="before"/>: something found lost, a lock locked again, a trap
    /// reset or seen no more, a failed way forgotten, or a state further back in its kind's order. Only a lever, and a passage a lever works
    /// (<paramref name="worked"/>), may change state both ways (design §2.3). The one sanctioned way back for anything else is a long rest
    /// (USER DECISION 2, decided 2026-11-03): a failed pick, force or seal and a failed search may be forgotten when <paramref name="after"/>
    /// was reopened at a later rest than <paramref name="before"/> (<see cref="Rested"/>); the rest it's kept at never goes back.
    /// </summary>
    public static bool Regressed(InteractableDef def, InteractableSave before, InteractableSave after, bool worked)
    {
        var rested = after.Rest > before.Rest;
        if ((before.Found && !after.Found) || (before.Unlocked && !after.Unlocked) || (before.TrapSpotted && !after.TrapSpotted)
            || (before.Trap is not null && after.Trap != before.Trap) || before.Spent.Except(after.Spent).Any(a => !(rested && Reopens(a)))
            || (before.Searched && !after.Searched && !rested) || after.Rest < before.Rest || (before.Keyed && !after.Keyed))
            return true;
        var kind = KindOf(def);
        if (kind.Family == InteractableFamily.Switch || (kind.Family == InteractableFamily.Passage && worked))
            return false;
        return kind.States.IndexOf(after.State) < kind.States.IndexOf(before.State);
    }
}
