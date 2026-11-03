using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Gathering at clear seats and sound fallbacks, without rebuilding the world or altering its saved location.</summary>
public sealed class RecoveryShow
{
    private readonly Dictionary<string, UnitPlace> _now = new(StringComparer.Ordinal);
    /// <summary>Night is a presentation-only step; votes and dawn use the normal region light.</summary>
    public static bool Night(CampView? camp) => camp is { Stage: not ("vote" or "dawn") };
    /// <summary>Move the pictured party towards the server's legal, unique seats at a walking pace.</summary>
    public IReadOnlyDictionary<string, UnitPlace>? Gather(CampView? camp, BoardTerrain terrain, IReadOnlyDictionary<string, UnitPlace> start, double seconds)
    {
        if (camp is not { Mode: "camp", Stage: not ("vote" or "dawn") }) { _now.Clear(); return null; }
        foreach (var person in camp.People)
        {
            var goal = UnitPlace.On(terrain, person.At);
            var from = _now.GetValueOrDefault(person.Member, start.GetValueOrDefault(person.Member, goal));
            var distance = Vector2.Distance(from.Map, goal.Map);
            var part = distance < .001f ? 1 : Math.Min(1, (float)(seconds * 2.5) / distance);
            _now[person.Member] = new UnitPlace(Vector2.Lerp(from.Map, goal.Map, part), from.Height + (goal.Height - from.Height) * part);
        }
        return _now;
    }
    /// <summary>The requested recovery sound followed by the existing-pack fallback.</summary>
    public static ImmutableArray<string> Wanted(string action) => action switch
    {
        "camp_start" => [action, "spell_fire_aura"], "camp_mend" => [action, "ui_level_up"],
        "camp_cook" => [action, "ui_confirm"], "camp_dawn" => [action, "ui_menu_open"],
        "temple_heal" => [action, "shrine_use"], "inn_room" => [action, "door_open"], _ => [],
    };
    /// <summary>The first available sound, or silence.</summary>
    public static string? Sound(string action, SoundBank? bank) => bank is null ? null : Wanted(action).FirstOrDefault(bank.Has);
}
