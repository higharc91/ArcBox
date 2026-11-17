using Meridian.Presentation.Board;

namespace Meridian.Presentation.Art;

/// <summary>A frame size an art set allows, in pixels.</summary>
public readonly record struct FrameSize(int W, int H);

/// <summary>
/// One art set of the contract (#331): where its manifest keeps its entries (<see cref="MapKey"/>), which ids it may and
/// must cover, and the frame sizes it allows (none: any size).
/// </summary>
/// <param name="Strict">An id outside <see cref="Ids"/> is an error (the set is keyed by content ids); otherwise a warning.</param>
public sealed record ArtSet(string Name, string MapKey, Func<RulesPack, IReadOnlyCollection<string>> Ids, IReadOnlyList<FrameSize> Frames,
                            bool Strict, bool NeedsInsets = false);

/// <summary>
/// The art contract as code (#331; M5 plan, Task 1). Every set is <c>content/meridian/base/&lt;set&gt;/manifest.json</c>; under
/// its map key, an object per id. An <b>entry</b> is any object with a <c>file</c>: a hash-named file, its <c>sha256</c>, its
/// <c>frameSize</c>, and for a strip its <c>frames</c> and <c>fps</c>, laid out left to right. An id's object may instead
/// hold named entries (states, animations, facings) or deck <c>variants</c>, nested as deep as the set needs.
/// </summary>
public static class ArtContract
{
    private static readonly FrameSize Tile = new(64, 64);
    private static readonly FrameSize Sprite = new(24, 24);

    public static readonly IReadOnlyList<string> HatchStates = ["open", "closed", "sealed", "none"];
    public static readonly IReadOnlyList<string> CardFrames = ["incident", "salvage", "anomaly"];

    /// <summary>The TV kit's pieces (#335).</summary>
    public static readonly IReadOnlyList<string> UiPieces =
    [
        "logo", "title", "panel", "seatSlot", "anomalyCounter", "roundClock", "logPanel", "cardPanel", "diceTray", "die", "miniMap",
        "breachPlate", "debriefPlate",
    ];

    /// <summary>The board effects (#337).</summary>
    public static readonly IReadOnlyList<string> Effects =
    [
        "reveal", "hatchOpen", "hatchSeal", "hit", "miss", "stagger", "stun", "traitLoss", "traitGain", "breachAlarm", "breachDistortion",
        "vent", "sparks", "rot", "pressure", "echo",
    ];

    /// <summary>The music and sound (#338). Tiller is text-to-speech, never a file.</summary>
    public static readonly IReadOnlyList<string> Sounds =
    [
        "music.explore", "music.breach", "music.endgame", "music.debriefWon", "music.debriefLost",
        "sfx.footstep", "sfx.hatchOpen", "sfx.hatchSeal", "sfx.reveal", "sfx.cardIncident", "sfx.cardSalvage", "sfx.cardAnomaly",
        "sfx.diceRoll", "sfx.diceSettle", "sfx.hit", "sfx.miss", "sfx.stagger", "sfx.death", "sfx.echo", "sfx.vent", "sfx.alarm", "sfx.tick",
    ];

    /// <summary>The token kinds the scenarios place, as the board draws them.</summary>
    public static readonly IReadOnlyList<string> TokenKinds = ["brood", "repair", "core", "engine", "pods", "prize", "item"];

    public static readonly IReadOnlyList<ArtSet> Sets =
    [
        new("tiles", "tiles", p => [.. p.StartTiles.Concat(p.Tiles).Select(t => t.Id.Value).Distinct()], [Tile], Strict: true),
        new("hatches", "states", _ => HatchStates, [Tile], Strict: true),
        new("crew", "crew", p => [.. p.Crew.Select(c => c.Id.Value)], [Sprite], Strict: true),
        new("portraits", "portraits", p => [.. p.Crew.Select(c => c.Id.Value)], [], Strict: true),
        new("cards", "frames", _ => CardFrames, [], Strict: true, NeedsInsets: true),
        new("icons", "icons", p => [.. p.Cards.Where(c => c.Kind != CardKind.Incident).Select(c => c.Id.Value), .. CardFrames],
            [new(16, 16), Sprite], Strict: true),
        new("tokens", "tokens", _ => TokenKinds, [Sprite, Tile], Strict: false),
        new("ui", "ui", _ => UiPieces, [], Strict: false),
        new("fx", "fx", _ => Effects, [new(32, 32), Tile], Strict: false),
        new("audio", "audio", _ => Sounds, [], Strict: false),
    ];

    public static ArtSet Set(string name) => Sets.First(s => s.Name == name);

    /// <summary>The decks a tile may have a variant for: the decks it can be placed on.</summary>
    public static IReadOnlySet<string> TileDecks(RulesPack pack, string tileId)
        => pack.StartTiles.Concat(pack.Tiles).FirstOrDefault(t => t.Id.Value == tileId) is { } tile
            ? tile.Decks.Select(d => BoardStyle.DeckName(d).ToLowerInvariant()).ToHashSet()
            : new HashSet<string>();
}
