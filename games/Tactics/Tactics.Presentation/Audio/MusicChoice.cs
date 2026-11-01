using System.Collections.Immutable;
using Tactics.Game;

namespace Tactics.Presentation.Audio;

/// <summary>A track to play.</summary>
/// <param name="Id">Its id in <c>music/manifest.json</c>.</param>
/// <param name="Then">What follows it, if it's a sting that ends.</param>
public sealed record MusicCue(string Id, string? Then = null);

/// <summary>
/// What plays for each screen, from the host view. A fight's music goes by its encounter. The manifest's town and regional tracks
/// aren't here, as the TV has no screen for them yet. Pure: a test holds every id here against the manifest.
/// </summary>
public static class MusicChoice
{
    private const string Title = "title";
    private const string Explore = "explore";
    private const string Fight = "battle";
    private const string Boss = "boss";
    private const string Finale = "finale";
    private const string Victory = "victory";
    private const string Defeat = "defeat";
    private const string Camp = "camp";

    private static readonly ImmutableDictionary<string, string> Fights = new Dictionary<string, string>
    {
        ["warlord_bridge"] = Boss,
        ["keep_gate"] = Boss,
        ["hall_of_the_flame"] = Finale,
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, string> Beds = new Dictionary<string, string>
    {
        ["river_crossing"] = "amb_river",
        ["forest_ruins"] = "amb_forest",
        ["crypt_hall"] = "amb_dungeon",
        ["crystal_cave"] = "amb_dungeon",
        ["warlord_bridge"] = "amb_plains_wind",
        ["keep_gate"] = "amb_plains_wind",
        ["hall_of_the_flame"] = "amb_lava",
    }.ToImmutableDictionary();

    /// <summary>Every track named here.</summary>
    public static IEnumerable<string> Ids => [Title, Explore, Fight, Boss, Finale, Victory, Defeat, Camp];

    /// <summary>The music for <paramref name="view"/>, or null to leave what's playing, as across a server restart.</summary>
    public static MusicCue? Of(TacticsHostView view)
    {
        if (!Enum.TryParse<TacticsPhase>(view.Phase, out var phase))
            return null;
        return phase switch
        {
            TacticsPhase.Muster => new MusicCue(Title),
            TacticsPhase.EncounterPick => new MusicCue(Explore),
            TacticsPhase.Battle when view.Battle is { } battle => new MusicCue(Fights.GetValueOrDefault(battle.Encounter, Fight)),
            TacticsPhase.Results => view.Outcome switch
            {
                "Won" => new MusicCue(Victory, Camp),
                "Lost" => new MusicCue(Defeat, Camp),
                _ => new MusicCue(Camp),
            },
            _ => null,
        };
    }

    /// <summary>The bed under <paramref name="view"/>'s fight, or null away from one.</summary>
    public static string? AmbienceOf(TacticsHostView view) => view.Phase == nameof(TacticsPhase.Battle) && view.Battle is { } battle ? Ambience(battle.Map) : null;

    /// <summary>The bed for a map, by its id in <c>sfx/manifest.json</c>, or null.</summary>
    public static string? Ambience(string map) => Beds.GetValueOrDefault(map);
}
