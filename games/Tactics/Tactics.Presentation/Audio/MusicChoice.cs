using System.Collections.Immutable;
using Tactics.Game;

namespace Tactics.Presentation.Audio;

/// <summary>A track to play.</summary>
/// <param name="Id">Its id in <c>music/manifest.json</c>.</param>
/// <param name="Then">What follows it, if it's a sting that ends.</param>
public sealed record MusicCue(string Id, string? Then = null);

/// <summary>
/// What plays for each screen, from the host view. A fight's music goes by its encounter, and walking's by its region, as the
/// manifest's own notes give them. Pure: a test holds every id here against the manifest.
/// </summary>
public static class MusicChoice
{
    private const string Title = "title";
    private const string Explore = "explore";
    private const string Fight = "battle";
    private const string Boss = "boss";
    private const string Finale = "finale";
    private const string Victory = "victory";
    private const string BossVictory = "victory_boss";
    private const string Defeat = "defeat";
    private const string Camp = "camp";
    private const string Dawn = "camp_dawn";
    private const string Emberwick = "town_emberwick";

    private static readonly ImmutableDictionary<string, string> Fights = new Dictionary<string, string>
    {
        ["warlord_bridge"] = Boss,
        ["keep_gate"] = Boss,
        ["rime_tower"] = Boss,          // Ivo Rime, Chapter 4's climax: the user's call, 2026-11-01 (Ford Assault stays ordinary)
        ["hall_of_the_flame"] = Finale,
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, string> Regions = new Dictionary<string, string>
    {
        ["ashen_ford"] = Emberwick,
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
    public static IEnumerable<string> Ids => [Title, Explore, Fight, Boss, Finale, Victory, BossVictory, Defeat, Camp, Dawn, Emberwick];

    /// <summary>The music for <paramref name="view"/>, or null to leave what's playing, as across a server restart.</summary>
    public static MusicCue? Of(TacticsHostView view)
    {
        if (!Enum.TryParse<TacticsPhase>(view.Phase, out var phase))
            return null;
        if (phase == TacticsPhase.Story && view.Camp is { Stage: "dawn" })
            return new MusicCue(Dawn, view.Explore is { } dawnPlace ? Regions.GetValueOrDefault(dawnPlace.Region, Explore) : Explore);
        if (phase == TacticsPhase.Story && view.Camp is { Stage: not ("vote" or "dawn") })
            return new MusicCue(Camp);
        return phase switch
        {
            TacticsPhase.Muster => new MusicCue(Title),
            TacticsPhase.EncounterPick => new MusicCue(Explore),
            TacticsPhase.Exploration or TacticsPhase.Story => new MusicCue(view.Explore is { } explore ? Regions.GetValueOrDefault(explore.Region, Explore) : Explore),
            TacticsPhase.Battle when view.Battle is { } battle => new MusicCue(Fights.GetValueOrDefault(battle.Encounter, Fight)),
            TacticsPhase.Results => view.Outcome switch
            {
                "Won" => new MusicCue(IsBoss(view.LastBattle) ? BossVictory : Victory, Camp),
                "Lost" => new MusicCue(Defeat, Camp),
                _ => new MusicCue(Camp),
            },
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="battle"/> was a fight with boss music, so winning it earns the grander fanfare.</summary>
    private static bool IsBoss(BattleView? battle) => battle is not null && Fights.ContainsKey(battle.Encounter);

    /// <summary>The bed under <paramref name="view"/>'s fight, or null away from one.</summary>
    public static string? AmbienceOf(TacticsHostView view) => view.Camp is { Mode: "camp", Stage: not ("vote" or "dawn") } ? "amb_campfire" : view.Phase == nameof(TacticsPhase.Battle) && view.Battle is { } battle ? Ambience(battle.Map) : null;

    /// <summary>The bed for a map, by its id in <c>sfx/manifest.json</c>, or null.</summary>
    public static string? Ambience(string map) => Beds.GetValueOrDefault(map);
}
