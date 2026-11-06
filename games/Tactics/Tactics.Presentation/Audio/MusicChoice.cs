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
    private const string Dungeon = "dungeon";
    private const string Fight = "battle";
    private const string Boss = "boss";
    private const string Finale = "finale";
    private const string Victory = "victory";
    private const string BossVictory = "victory_boss";
    private const string Defeat = "defeat";
    private const string Camp = "camp";
    private const string Dawn = "camp_dawn";
    private const string Emberwick = "town_emberwick";
    private const string Mirewood = "court_mirewood";
    private const string ScarwalkerCamp = "camp_scarwalker";
    private const string Greyhold = "town_greyhold";
    private const string Festival = "town_emberwick_festival";

    private static readonly ImmutableDictionary<string, string> Fights = new Dictionary<string, string>
    {
        ["warlord_bridge"] = Boss,
        ["hollow_lair"] = Boss,
        ["keep_gate"] = Boss,
        ["rime_tower"] = Boss,          // Ivo Rime, Chapter 4's climax: the user's call, 2026-11-01 (Ford Assault stays ordinary)
        ["hall_of_the_flame"] = Finale,
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, string> Regions = new Dictionary<string, string>
    {
        ["ashen_ford"] = Emberwick,
        ["cinder_hollow"] = Dungeon,
        ["mirewood"] = Mirewood,
        ["frostpeak"] = Greyhold,
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, string> Beds = new Dictionary<string, string>
    {
        ["river_crossing"] = "amb_river",
        ["mob_pine_wolves"] = "amb_forest", ["mob_rooting_boars"] = "amb_forest",
        ["mob_kobold_scouts"] = "amb_plains_wind", ["mob_river_slimes"] = "amb_river",
        ["mob_road_bandits"] = "amb_plains_wind",
        ["mob_hollow_bats"] = "amb_cinder_hollow", ["mob_hollow_kobolds"] = "amb_cinder_hollow",
        ["forest_ruins"] = "amb_forest",
        ["crypt_hall"] = "amb_dungeon",
        ["crystal_cave"] = "amb_dungeon",
        ["warlord_bridge"] = "amb_plains_wind",
        ["keep_gate"] = "amb_plains_wind",
        ["hall_of_the_flame"] = "amb_lava",
        ["hollow_throat"] = "amb_cinder_hollow",
        ["hollow_diggings"] = "amb_cinder_hollow",
        ["hollow_nest"] = "amb_cinder_hollow",
        ["hollow_lair"] = "amb_cinder_hollow",
    }.ToImmutableDictionary();

    /// <summary>Every track named here.</summary>
    public static IEnumerable<string> Ids => [Title, Explore, Dungeon, Fight, Boss, Finale, Victory, BossVictory, Defeat, Camp, Dawn,
        Emberwick, Mirewood, ScarwalkerCamp, Greyhold, Festival];

    /// <summary>The music for <paramref name="view"/>, or null to leave what's playing, as across a server restart.</summary>
    public static MusicCue? Of(TacticsHostView view)
    {
        if (!Enum.TryParse<TacticsPhase>(view.Phase, out var phase))
            return null;
        if (phase == TacticsPhase.Story && view.Story?.Scene == "ch5_epilogue_rekindled")
            return new MusicCue(Festival);
        if (phase == TacticsPhase.Story && view.Camp is { Stage: "dawn" })
            return new MusicCue(Dawn, view.Explore is { } dawnPlace ? Regions.GetValueOrDefault(dawnPlace.Region, Explore) : Explore);
        if (phase == TacticsPhase.Story && view.Camp is { Stage: not ("vote" or "dawn") })
            return new MusicCue(view.Explore?.Region == "sunscar" ? ScarwalkerCamp : Camp);
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

    /// <summary>The bed for the current battle or walking place. Rain replaces the dry place bed while its weather layer is active.</summary>
    public static string? AmbienceOf(TacticsHostView view)
    {
        if (view.Camp is { Mode: "camp", Stage: not ("vote" or "dawn") }) return "amb_campfire";
        if (view.Camp is not null) return null;
        if (view.Phase == nameof(TacticsPhase.Battle) && view.Battle is { } battle) return Ambience(battle.Map);
        if (view.Phase is not (nameof(TacticsPhase.Exploration) or nameof(TacticsPhase.Story)) || view.Explore is not { } explore)
            return null;
        if (explore.Weather is "rain" or "storm" or "drizzle") return "amb_rain";
        return explore.Region switch
        {
            "ashen_ford" => FordBed(explore.At.X, explore.At.Y),
            "cinder_hollow" => "amb_cinder_hollow",
            "mirewood" => "amb_swamp",
            _ => null,
        };
    }

    // Ashen Ford's town is west of the river; the docks, bridge and banks share the river bed.
    // The remaining fields, hills and roads hear open wind.
    private static string FordBed(int x, int y) =>
        x is >= 14 and <= 55 && y is >= 44 and <= 78 ? "amb_town" :
        x is >= 88 and <= 123 ? "amb_river" : "amb_plains_wind";

    /// <summary>The bed for a map, by its id in <c>sfx/manifest.json</c>, or null.</summary>
    public static string? Ambience(string map) => Beds.GetValueOrDefault(map);

    /// <summary>A quiet creature bed near the lair, separate from the cave's room tone. Never plays after the kill.</summary>
    public static string? CreatureAmbienceOf(TacticsHostView view)
    {
        if (view.Battle is { Encounter: "hollow_lair" } battle)
            return battle.Units.Any(u => u.Id.Value == "ashmaw" && u.Hp > 0) ? "ashmaw_breath_loop" : null;
        if (view.Phase is not (nameof(TacticsPhase.Exploration) or nameof(TacticsPhase.Story))
            || view.Story?.Scene == "ashmaw_lair_after" || view.Explore is not { Region: "cinder_hollow" } explore
            || explore.PeopleFlags?.Contains("sq_ashmaw_slain") == true) return null;
        return Math.Abs(explore.At.X - 10) + Math.Abs(explore.At.Y - 32) <= 8 ? "ashmaw_breath_loop" : null;
    }
}
