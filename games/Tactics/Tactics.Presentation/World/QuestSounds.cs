using System.Collections.Immutable;

namespace Tactics.Presentation.World;

/// <summary>Logical cues with existing-pack fallbacks. No new sound assets are required.</summary>
public static class QuestSounds
{
    public static ImmutableArray<string> Wanted(string cue) => cue switch
    {
        "start" => ["quest_start", "secret_found"],
        "update" => ["quest_update", "page_turn"],
        "complete" => ["quest_complete", "ui_level_up"],
        "talk" => ["talk_start", "ui_menu_open"],
        "open" => ["questlog_open", "page_turn"],
        "close" => ["questlog_close", "page_turn"],
        "pin" => ["questlog_pin", "ui_select"],
        "accept" => ["bounty_accept", "quest_start", "secret_found"],
        "present" => ["trinket_presented", "quest_complete", "ui_level_up"],
        "enter" => ["cave_enter", "door_open"],
        _ => [],
    };
}
