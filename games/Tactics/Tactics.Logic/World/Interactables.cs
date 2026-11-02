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
    /// True when it stands in the way of walking (design §2.4): a passage while it's closed (found or not: a hidden door is a wall), a
    /// floor trap that's been seen and is still set (the party walks round it), and otherwise as its kind or content says. Something that
    /// isn't there blocks nothing.
    /// </summary>
    public static bool Blocks(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
    {
        if (!Present(def, flags))
            return false;
        var kind = KindOf(def);
        return kind.Family switch
        {
            InteractableFamily.Passage => save.State == InteractableKinds.Closed,
            InteractableFamily.FloorTrap => save.TrapSpotted && save.Trap is null,
            _ => def.Blocks ?? kind.Blocks,
        };
    }

    /// <summary>
    /// True for a shut passage nobody has found (a secret door): it blocks, but the TV isn't told it's there, so the TV is sent a plug of wall
    /// for its tile instead (<c>ExploreView.Plugs</c>), or its gap would give it away.
    /// </summary>
    public static bool Plugged(InteractableDef def, InteractableSave save, IReadOnlySet<string> flags)
        => KindOf(def).Family == InteractableFamily.Passage && Blocks(def, save, flags) && !Visible(def, save, flags);

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
        return kind.Family switch
        {
            InteractableFamily.Container when save.State == InteractableKinds.Closed => $"Open the {name}",
            InteractableFamily.Passage when save.State == InteractableKinds.Closed => worked ? $"Look at the {name}" : $"Open the {name}",
            InteractableFamily.Switch => $"Pull the {name}",
            InteractableFamily.Readable => $"Read the {name}",
            InteractableFamily.Pickup when save.State == InteractableKinds.Here => $"Pick up the {name}",
            InteractableFamily.Shrine when save.State == InteractableKinds.Ready => kind.Id == "well" ? $"Draw from the {name}" : $"Kneel at the {name}",
            InteractableFamily.Ambient => $"Look at the {name}",
            InteractableFamily.FloorTrap when save.TrapSpotted && save.Trap is null => $"Disarm the {name}",
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
    public static int Passive(UnitSheet sheet) => 10 + SkillCheck.Bonus(sheet, "perception");

    /// <summary>What a passive Perception of <paramref name="passive"/> finds from <paramref name="at"/>.</summary>
    public enum Finding
    {
        /// <summary>A hidden thing is found.</summary>
        Revealed,
        /// <summary>A trap is seen.</summary>
        Spotted,
    }

    /// <summary>
    /// What the party's best passive Perception <paramref name="passive"/> finds with the leader at <paramref name="at"/> (design §2.6): every
    /// hidden thing there and not yet found, and every trap still set and not yet seen, within <see cref="RevealRange"/> tiles along x and
    /// y, whose DC it meets. A trap on a hidden thing is seen only once the thing is found, which can be in the same pass. In id order.
    /// </summary>
    public static ImmutableArray<(InteractableDef Def, Finding Found)> Finds(IEnumerable<InteractableDef> defs,
        IReadOnlyDictionary<string, InteractableSave>? saves, IReadOnlySet<string> flags, Tile at, int passive)
    {
        var found = new List<(InteractableDef, Finding)>();
        foreach (var def in defs.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (!Present(def, flags) || Math.Max(Math.Abs(at.X - def.At.X), Math.Abs(at.Y - def.At.Y)) > RevealRange)
                continue;
            var save = StateOf(def, saves);
            var seen = def.Hidden is null || save.Found;
            if (!seen && passive >= def.Hidden!.Value)
            {
                found.Add((def, Finding.Revealed));
                seen = true;
            }
            if (seen && def.Trap is { } trap && save.Trap is null && !save.TrapSpotted && passive >= trap.Spot)
                found.Add((def, Finding.Spotted));
        }
        return [.. found];
    }

    /// <summary>
    /// True when <paramref name="after"/> has gone back on <paramref name="before"/>: something found lost, a lock locked again, a trap
    /// reset or seen no more, a failed way forgotten, or a state further back in its kind's order. Only a lever, and a passage a lever works
    /// (<paramref name="worked"/>), may change state both ways (design §2.3).
    /// </summary>
    public static bool Regressed(InteractableDef def, InteractableSave before, InteractableSave after, bool worked)
    {
        if ((before.Found && !after.Found) || (before.Unlocked && !after.Unlocked) || (before.TrapSpotted && !after.TrapSpotted)
            || (before.Trap is not null && after.Trap != before.Trap) || before.Spent.Except(after.Spent).Any())
            return true;
        var kind = KindOf(def);
        if (kind.Family == InteractableFamily.Switch || (kind.Family == InteractableFamily.Passage && worked))
            return false;
        return kind.States.IndexOf(after.State) < kind.States.IndexOf(before.State);
    }
}
