using System.Globalization;
using Tactics.Game;

namespace Tactics.Presentation;

/// <summary>Turning content ids into words for the TV.</summary>
public static class Words
{
    /// <summary>A content id as a title: <c>river_crossing</c> → "River Crossing".</summary>
    public static string Title(string id)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));

    /// <summary>
    /// A monster's name where there's little room: its last word, which is what tells a Goblin Boss from a Goblin Archer. It's
    /// not for a character, whose name was chosen and has no such pattern ("Ana Smith" isn't "Smith").
    /// </summary>
    public static string Short(string name)
        => name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? name;

    /// <summary>The encounter's own name, or for a view that has none, its id as a title.</summary>
    public static string Encounter(BattleView battle) => battle.Name ?? Title(battle.Encounter);
}
