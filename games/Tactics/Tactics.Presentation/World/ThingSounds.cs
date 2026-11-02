using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Audio;

namespace Tactics.Presentation.World;

/// <summary>
/// The sound of something happening to a thing (interactables design §6 question 5), by the sfx manifest's ids. Each event names the sounds
/// it wants, the best first; the first the pack has plays, and with none it's silent. The pack has no door, chest, lever or trap sounds yet,
/// so most of these are silent until sounds with these ids are added to <c>sfx/manifest.json</c>, which then play with no code change.
/// </summary>
public static class ThingSounds
{
    /// <summary>How loud they play, in dB: under the voices, over the footsteps.</summary>
    public const float Db = -8;

    private static readonly ImmutableDictionary<string, ImmutableArray<string>> Opening = new Dictionary<string, ImmutableArray<string>>
    {
        ["door"] = ["door_open"],
        ["gate"] = ["gate_open", "door_open"],
        ["drawbridge"] = ["drawbridge_lower", "gate_open"],
        ["secret_door"] = ["secret_door_open", "door_open"],
        ["chest"] = ["chest_open"],
        ["crate"] = ["crate_open", "chest_open"],
        ["barrel"] = ["barrel_open", "crate_open"],
        ["sack"] = ["sack_open"],
        ["cache"] = ["cache_open", "chest_open"],
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, ImmutableArray<string>> Closing = new Dictionary<string, ImmutableArray<string>>
    {
        ["door"] = ["door_close"],
        ["gate"] = ["gate_close", "door_close"],
        ["drawbridge"] = ["drawbridge_raise", "gate_close"],
        ["secret_door"] = ["secret_door_close", "door_close"],
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableDictionary<string, ImmutableArray<string>> Using = new Dictionary<string, ImmutableArray<string>>
    {
        ["shrine"] = ["shrine_use"],
        ["well"] = ["well_draw"],
        ["altar"] = ["altar_use", "shrine_use"],
    }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The sounds <paramref name="what"/> wants for a thing of <paramref name="kind"/> that's now in <paramref name="state"/>, the best first:
    /// a passage a lever moves sounds as it opens or shuts.
    /// </summary>
    public static ImmutableArray<string> Wanted(string what, string kind, string state) => what switch
    {
        ThingEvents.Opened => Opening.GetValueOrDefault(kind, []),
        ThingEvents.Toggled when kind == "lever" => ["lever_pull"],
        ThingEvents.Toggled => state == InteractableKinds.Opened ? Opening.GetValueOrDefault(kind, []) : Closing.GetValueOrDefault(kind, []),
        ThingEvents.Unlocked => ["lock_open"],
        ThingEvents.Failed => ["lock_fail"],
        ThingEvents.Revealed => ["secret_found"],
        ThingEvents.Spotted => ["trap_spotted"],
        ThingEvents.Sprung => ["trap_spring"],
        ThingEvents.Disarmed => ["trap_disarm"],
        ThingEvents.Taken => ["item_pickup", "ui_loot"],
        ThingEvents.Read => ["page_turn"],
        ThingEvents.Used => Using.GetValueOrDefault(kind, []),
        _ => [],
    };

    /// <summary>The sound to play, or null for silence: the first of <see cref="Wanted"/> that <paramref name="bank"/> has.</summary>
    public static string? For(string what, string kind, string state, SoundBank? bank)
        => bank is null ? null : Wanted(what, kind, state).FirstOrDefault(bank.Has);

    /// <summary>Every id any event wants, once each, in order: for the list of sounds still to make.</summary>
    public static ImmutableArray<string> All()
    {
        string[] events = [ThingEvents.Opened, ThingEvents.Toggled, ThingEvents.Unlocked, ThingEvents.Failed, ThingEvents.Revealed, ThingEvents.Spotted,
            ThingEvents.Sprung, ThingEvents.Disarmed, ThingEvents.Taken, ThingEvents.Read, ThingEvents.Used, ThingEvents.Looked];
        string[] states = [InteractableKinds.Opened, InteractableKinds.Closed];
        return [.. events.SelectMany(e => InteractableKinds.All.Keys.SelectMany(k => states.SelectMany(s => Wanted(e, k, s)))).Distinct(StringComparer.Ordinal)];
    }
}
