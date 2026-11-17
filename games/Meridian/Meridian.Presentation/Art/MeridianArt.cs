using Meridian.Presentation.Board;

namespace Meridian.Presentation.Art;

/// <summary>A tile's art: a 64 × 64 frame, lit from overhead so the engine can turn it.</summary>
public sealed record TileArt(string Path);

/// <summary>A crew member's sprites: 24 × 24 frames anchored at the feet.</summary>
public sealed record CrewArt(string Idle, string Walk, int WalkFrames, int Fps, int AnchorX, int AnchorY);

/// <summary>
/// The art the content pack has (the contract in #331), as <see cref="ArtCheck"/> passed it: anything missing or rejected
/// isn't here, and the scene draws its placeholder at the same size (M4 plan, Task 6; M5 plan, Task 1).
/// <see cref="Problems"/> lists what was refused, for one log line each.
/// </summary>
public sealed class MeridianArt
{
    private readonly IReadOnlyDictionary<string, ArtEntry> _entries;

    private MeridianArt(IReadOnlyDictionary<string, ArtEntry> entries, IReadOnlyList<string> problems, IReadOnlyList<ArtCoverage> coverage)
        => (_entries, Problems, Coverage) = (entries, problems, coverage);

    /// <summary>No art at all: everything is a placeholder.</summary>
    public static MeridianArt None { get; } = new(new Dictionary<string, ArtEntry>(), [], []);

    public IReadOnlyList<string> Problems { get; }
    public IReadOnlyList<ArtCoverage> Coverage { get; }

    /// <param name="root">The content pack's directory (<c>content/meridian/base</c> as delivered).</param>
    public static MeridianArt Load(string root, RulesPack pack)
    {
        var check = ArtCheck.Run(root, pack);
        return new MeridianArt(check.Entries, [.. check.Problems.Select(p => p.ToString())], check.Coverage);
    }

    /// <summary>Any passed entry by its key (<c>set/id[/name…]</c>), or null.</summary>
    public ArtEntry? Entry(string key) => _entries.GetValueOrDefault(key);

    /// <summary>
    /// A tile's art for a deck: its variant for that deck, else its single art, else none (M5 review focus 3: never another
    /// deck's variant).
    /// </summary>
    public TileArt? Tile(string tileId, DeckId? deck = null)
    {
        if (deck is { } d && Entry($"tiles/{tileId}/variants/{BoardStyle.DeckName(d).ToLowerInvariant()}") is { } variant)
        {
            return new TileArt(variant.Path);
        }
        return Entry($"tiles/{tileId}") is { } single ? new TileArt(single.Path) : null;
    }

    /// <summary>A hatch overlay, drawn for the north edge. <see cref="HatchState.None"/> is a wall's overlay.</summary>
    public string? Hatch(HatchState state) => Entry($"hatches/{state.ToString().ToLowerInvariant()}")?.Path;

    public CrewArt? Crew(string crewId)
    {
        if (Entry($"crew/{crewId}/idle") is not { } idle || Entry($"crew/{crewId}/walk") is not { } walk)
        {
            return null;
        }
        var anchor = idle.Anchor ?? walk.Anchor ?? [12, 22];
        return new CrewArt(idle.Path, walk.Path, walk.Frames, walk.Fps, anchor[0], anchor[1]);
    }
}
