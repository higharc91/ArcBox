using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>How a kind of thing behaves: which of the engine's state machines it runs (design §2.2).</summary>
public enum InteractableFamily
{
    /// <summary>Opens once and gives up its loot: chests, crates, barrels, sacks, hidden caches.</summary>
    Container,
    /// <summary>Stands in the way while closed: doors, gates, drawbridges, secret doors.</summary>
    Passage,
    /// <summary>Flips between off and on, working its passages: levers.</summary>
    Switch,
    /// <summary>Shows its words on the TV and the phones: notes, plaques, books, gravestones, signposts.</summary>
    Readable,
    /// <summary>Lies on the ground until it's picked up.</summary>
    Pickup,
    /// <summary>Used once, for its words and its flags: shrines, wells, altars.</summary>
    Shrine,
    /// <summary>Says one line when looked at, and never changes.</summary>
    Ambient,
    /// <summary>A trap on the ground: sprung by a step, unless it's seen first.</summary>
    FloorTrap,
    /// <summary>
    /// Something only ever noticed, never used beyond a look (design §7.2): tracks, scratches, disturbed earth, a bloodstain, a draft, a carving,
    /// a rune, remains. Always hidden; noticing it says its words, sets its flags and finds what it <see cref="InteractableDef.Reveals"/>.
    /// </summary>
    Notice,
    /// <summary>A hidden path (design §7.3): a tile that stands in the way until it's found, then is walked like any ground.</summary>
    Path,
}

/// <summary>
/// A kind of thing to interact with: its family, its name and art, the states its art is drawn in, whether it stands in the way, and what it
/// says when its content says nothing (design §2.2). <see cref="StandIn"/> is an art key the pack already has, for the TV to draw until
/// <see cref="Art"/> is made; null means a plain marker.
/// </summary>
/// <param name="Id">The kind's id, as content names it.</param>
/// <param name="Family">Its state machine.</param>
/// <param name="Name">What one is called ("chest"), when its content names it nothing.</param>
/// <param name="Art">Its art key, without a state (<c>interactables.chest</c>); the state is the variant.</param>
/// <param name="States">The states it can be in, in order: the first is where it starts, unless <see cref="InteractableDef.Open"/>.</param>
/// <param name="Blocks">Whether one stands in the way. A passage's state decides instead.</param>
/// <param name="StandIn">An art key the pack has now, by state, for the TV meanwhile; a missing state draws the first's, and none at all a plain marker.</param>
/// <param name="Text">What it says when its content says nothing.</param>
/// <param name="MustHide">True for a kind that's always hidden at first (a cache, a secret door): its content must give <see cref="InteractableDef.Hidden"/>.</param>
/// <param name="Empty">The art states that draw nothing at all (a pickup taken): the TV isn't sent the thing in them.</param>
public sealed record InteractableKind(string Id, InteractableFamily Family, string Name, string Art, ImmutableArray<string> States, bool Blocks,
    ImmutableSortedDictionary<string, string> StandIn, ImmutableArray<string> Text, bool MustHide = false, ImmutableArray<string> Empty = default)
{
    /// <summary>The art states that draw nothing; never a default array.</summary>
    public ImmutableArray<string> Empty { get => field.IsDefault ? [] : field; init; } = Empty;
}

/// <summary>
/// The library of kinds (design §2.2). It's code, not content, because each kind is one of the engine's state machines with its defaults; a
/// content pack picks a kind and overrides its name, art, words and blocking per thing. The art keys are the art brief's (design §4).
/// </summary>
public static class InteractableKinds
{
    /// <summary>A closed container or passage.</summary>
    public const string Closed = "closed";

    /// <summary>An open container or passage.</summary>
    public const string Opened = "open";

    /// <summary>A lever that's off.</summary>
    public const string Off = "off";

    /// <summary>A lever that's on.</summary>
    public const string On = "on";

    /// <summary>A readable nobody has read yet.</summary>
    public const string Unread = "unread";

    /// <summary>A readable that's been read.</summary>
    public const string Read = "read";

    /// <summary>A pickup still on the ground.</summary>
    public const string Here = "here";

    /// <summary>A pickup that's been taken.</summary>
    public const string Taken = "taken";

    /// <summary>A shrine not yet used.</summary>
    public const string Ready = "ready";

    /// <summary>A shrine that's been used.</summary>
    public const string Used = "used";

    /// <summary>The one state of an ambient object, and of a floor trap (whose trap is in <see cref="InteractableSave.Trap"/>).</summary>
    public const string Idle = "idle";

    /// <summary>The one state of a notice: it's drawn only once it's been noticed (<see cref="InteractableSave.Found"/>).</summary>
    public const string Noticed = "noticed";

    /// <summary>The one state of a hidden path: it's drawn only once it's been found.</summary>
    public const string FoundPath = "found";

    private static ImmutableSortedDictionary<string, string> Stand(params (string State, string Key)[] keys)
        => keys.ToImmutableSortedDictionary(k => k.State, k => k.Key, StringComparer.Ordinal);

    /// <summary>
    /// A container. Its stand-in is named for each state on purpose: the pack has an open chest, but no open crate, barrel or sack, so those
    /// are drawn as they stand (lid and all) until their art comes; an emptied cache draws nothing.
    /// </summary>
    private static InteractableKind Container(string id, string name, bool blocks, string closed, string? open, string line, bool hide = false)
        => new(id, InteractableFamily.Container, name, $"interactables.{id}", [Closed, Opened], blocks,
            open is null ? Stand((Closed, closed)) : Stand((Closed, closed), (Opened, open)), [line], hide, open is null ? [Opened] : []);

    private static InteractableKind Passage(string id, string name, string line, bool hide = false)
        => new(id, InteractableFamily.Passage, name, $"interactables.{id}", [Closed, Opened], false, Stand(), [line], hide);

    private static InteractableKind Readable(string id, string name, bool blocks, string? standIn)
        => new(id, InteractableFamily.Readable, name, $"interactables.{id}", [Unread, Read], blocks,
            standIn is null ? Stand() : Stand((Unread, standIn), (Read, standIn)), []);

    private static InteractableKind Shrine(string id, string name, string standIn, string line)
        => new(id, InteractableFamily.Shrine, name, $"interactables.{id}", [Ready, Used], true, Stand((Ready, standIn), (Used, standIn)), [line]);

    /// <summary>A notice: always hidden, never in the way, its words content's. Its stand-in is a prop the pack has for it, or the plain marker.</summary>
    private static InteractableKind Notice(string id, string name, string line, string? standIn = null)
        => new(id, InteractableFamily.Notice, name, $"interactables.{id}", [Noticed], false, standIn is null ? Stand() : Stand((Noticed, standIn)), [line],
            MustHide: true);

    /// <summary>Every kind, by id.</summary>
    public static ImmutableSortedDictionary<string, InteractableKind> All { get; } = new[]
    {
        Container("chest", "chest", true, "props.chest", "props.chest_open", "It's empty."),
        Container("crate", "crate", true, "props.crate", "props.crate", "Nothing but straw."),
        Container("barrel", "barrel", true, "props.barrel", "props.barrel", "Rainwater, and nothing else."),
        Container("sack", "sack", false, "props.sack", "props.sack", "Old grain."),
        Container("cache", "hidden cache", false, "pickups.loot_sack", null, "Someone hid this, and took it back.", hide: true),
        Passage("door", "door", "The door is shut."),
        Passage("gate", "gate", "The gate is shut."),
        Passage("drawbridge", "drawbridge", "The bridge is raised."),
        Passage("secret_door", "hidden door", "A seam in the stone.", hide: true),
        new InteractableKind("lever", InteractableFamily.Switch, "lever", "interactables.lever", [Off, On], false, Stand(), ["Clunk."]),
        Readable("note", "note", false, null),
        Readable("plaque", "plaque", false, null),
        Readable("book", "book", false, null),
        Readable("gravestone", "gravestone", true, "props.gravestone"),
        Readable("signpost", "signpost", false, "props.signpost"),
        new InteractableKind("pickup", InteractableFamily.Pickup, "something on the ground", "interactables.pickup", [Here, Taken], false,
            Stand((Here, "pickups.loot_sack")), [], Empty: [Taken]),
        Shrine("shrine", "shrine", "props.shrine", "The shrine is quiet now."),
        Shrine("well", "well", "props.well", "The water is cold and clear."),
        Shrine("altar", "altar", "props.altar", "The candles have burnt low."),
        new InteractableKind("ambient", InteractableFamily.Ambient, "something", "interactables.ambient", [Idle], false, Stand(), ["Nothing to see."]),
        new InteractableKind("trap", InteractableFamily.FloorTrap, "trap", "interactables.trap", [Idle], false, Stand(), ["Click."]),
        Notice("tracks", "tracks", "Tracks in the dirt."),
        Notice("scratches", "scratches", "Scratches, deep and fresh."),
        Notice("disturbed_earth", "disturbed earth", "The earth here has been dug and put back."),
        Notice("bloodstain", "bloodstain", "Old blood, dried brown."),
        Notice("draft", "draft", "A breath of cold air, from the stone itself."),
        Notice("carving", "carving", "Something is carved here."),
        Notice("rune", "rune", "A rune, faint as frost."),
        Notice("remains", "remains", "Bones, picked clean.", "props.ribcage"),
        new InteractableKind("hidden_path", InteractableFamily.Path, "hidden path", "interactables.hidden_path", [FoundPath], false, Stand(),
            ["A way through."], MustHide: true),
    }.ToImmutableSortedDictionary(k => k.Id, k => k, StringComparer.Ordinal);

    /// <summary>The kind with this id, or null.</summary>
    public static InteractableKind? Of(string id) => All.GetValueOrDefault(id);
}
