using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// The things to interact with in one region, as written in <c>interactables/&lt;region&gt;.json</c> (interactables design §2.1). They're
/// hand-written, beside the region file the painting workspace generates, so an export never loses them. Several packs may each add a file
/// for one region; ids are unique across the whole pack, because <see cref="InteractableSave"/>s are kept by id.
/// </summary>
public sealed record InteractablesFile(string Region, ImmutableArray<InteractableDef> Interactables);

/// <summary>
/// A lock (design §1.2): each way through it that's written is offered. <see cref="Key"/> is a story flag the party holds as its key, named
/// <see cref="KeyName"/> on the button; <see cref="Pick"/> is a Sleight of Hand DC, <see cref="Force"/> an Athletics DC and <see cref="Seal"/>
/// an Arcana DC for a magic seal. A check that fails is spent for this lock (design, USER DECISION 2), so only a key is sure to open it.
/// </summary>
public sealed record LockDef(string? Key = null, string? KeyName = null, int? Pick = null, int? Force = null, int? Seal = null)
{
    /// <summary>The ways through it, in the order they're offered: the key, then the checks.</summary>
    [JsonIgnore]
    public IEnumerable<(string Approach, string? Skill, int? Dc)> Approaches
    {
        get
        {
            if (Key is not null)
                yield return (Interactables.KeyApproach, null, null);
            if (Pick is { } pick)
                yield return (Interactables.PickApproach, "sleight_of_hand", pick);
            if (Force is { } force)
                yield return (Interactables.ForceApproach, "athletics", force);
            if (Seal is { } seal)
                yield return (Interactables.SealApproach, "arcana", seal);
        }
    }
}

/// <summary>
/// A trap on a container, a door or the ground (design §1.6). The party's best passive Perception against <see cref="Spot"/> finds it as the
/// leader passes within <see cref="Interactables.RevealRange"/>; <see cref="Disarm"/> is the Sleight of Hand DC to take it apart, null for
/// a trap that can't be. Springing it says <see cref="Text"/> and sets <see cref="Sets"/>; by default that's all it does (design, USER
/// DECISION 3). <see cref="Injury"/> gives the one who sprang it that injury, and <see cref="Lose"/> takes one of that item from their bag:
/// both are supported and unused until the user decides.
/// </summary>
public sealed record TrapDef([property: JsonRequired] int Spot, int? Disarm = null, ImmutableArray<string> Text = default,
    ImmutableArray<string> Sets = default, string? Injury = null, ItemId? Lose = null);

/// <summary>An item found in a container or on the ground: <see cref="Count"/> of the rules pack's <see cref="Item"/>.</summary>
public sealed record LootDef([property: JsonRequired] ItemId Item, int Count = 1);

/// <summary>
/// One thing to interact with (design §2.1). <see cref="Kind"/> names a kind of <see cref="InteractableKinds"/>, which gives everything
/// left out. It stands on <see cref="At"/>, facing <see cref="Facing"/> (<c>ne</c>, <c>se</c>, <c>sw</c> or <c>nw</c>, the four ways an
/// isometric sprite can face); it's there only while every flag of <see cref="Requires"/> is set and none of <see cref="Unless"/>.
/// </summary>
/// <param name="Id">Unique across the pack: its saved state is kept by it.</param>
/// <param name="Kind">The kind, from <see cref="InteractableKinds"/>.</param>
/// <param name="At">Its world tile.</param>
/// <param name="Facing">Which way its sprite faces; the kind's default when null.</param>
/// <param name="Name">What it's called on the phone and the TV ("the old chest"); the kind's name when null.</param>
/// <param name="Art">Its art key, without a state; the kind's when null.</param>
/// <param name="Text">What it says: a readable's words, an ambient object's bark, a shrine's lines. The kind's when empty.</param>
/// <param name="Requires">Flags that must all be set for it to be there.</param>
/// <param name="Unless">Flags that take it away when any is set.</param>
/// <param name="Sets">Flags its main outcome sets: opened, read, taken, used, or a lever first turned on.</param>
/// <param name="Lock">Its lock, for a container or a passage.</param>
/// <param name="Trap">Its trap: on a container or a passage, or what a floor trap is.</param>
/// <param name="Hidden">The passive Perception DC that finds it; null when it's in plain sight.</param>
/// <param name="Loot">What a container holds, or a pickup is.</param>
/// <param name="Targets">For a lever: the passages it opens when on and shuts when off.</param>
/// <param name="Open">True for a passage that starts open, or a lever that starts on.</param>
/// <param name="Blocks">Whether it stands in the way; the kind's when null. Never for a passage, whose state decides.</param>
public sealed record InteractableDef(string Id, string Kind, [property: JsonRequired] Tile At, string? Facing = null, string? Name = null,
    string? Art = null, ImmutableArray<string> Text = default, ImmutableArray<string> Requires = default, ImmutableArray<string> Unless = default,
    ImmutableArray<string> Sets = default, LockDef? Lock = null, TrapDef? Trap = null, int? Hidden = null,
    ImmutableArray<LootDef> Loot = default, ImmutableArray<string> Targets = default, bool Open = false, bool? Blocks = null);

/// <summary>
/// How a thing to interact with is, as the campaign keeps it (design §2.3). Every field only moves one way, except a lever's
/// <see cref="State"/> and the state of a passage a lever works (<see cref="Interactables.Settled"/>).
/// </summary>
/// <param name="State">The kind's state: see <see cref="InteractableKind.States"/>.</param>
/// <param name="Found">True once a hidden thing has been found.</param>
/// <param name="Unlocked">True once its lock is open.</param>
/// <param name="TrapSpotted">True once its trap has been seen.</param>
/// <param name="Trap">Null while its trap is set; <c>disarmed</c> or <c>sprung</c> after.</param>
/// <param name="Spent">The ways through its lock, or at its trap, that have failed (<see cref="Interactables.PickApproach"/> and the rest).</param>
public sealed record InteractableSave(string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Found = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Unlocked = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool TrapSpotted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Trap = null,
    ImmutableArray<string> Spent = default)
{
    /// <summary>The ways that have failed; never a default array.</summary>
    public ImmutableArray<string> Spent { get => field.IsDefault ? [] : field; init; } = Spent;

    /// <summary>A trap taken apart.</summary>
    public const string Disarmed = "disarmed";

    /// <summary>A trap gone off.</summary>
    public const string Sprung = "sprung";
}
