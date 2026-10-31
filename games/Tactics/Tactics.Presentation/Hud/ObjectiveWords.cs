using Tactics.Logic;

namespace Tactics.Presentation.Hud;

/// <summary>
/// A battle's objectives in words for the TV: "Defeat the warlord, or survive 11 rounds". Only the win expression is worded,
/// since every encounter's lose is the default of the party falling, which goes without saying.
/// </summary>
public static class ObjectiveWords
{
    private const string Or = ", or ";
    private const string And = ", and ";

    /// <summary>The sentence for <paramref name="objectives"/>, or null if they say nothing.</summary>
    public static string? Of(ObjectivesDef? objectives)
    {
        var words = objectives is null ? "" : Phrase(objectives.Win);
        return words.Length == 0 ? null : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Phrase(ObjectiveExpr expr)
    {
        if (expr.Leaf is { } leaf)
            return Leaf(leaf);
        if (!expr.Any.IsDefaultOrEmpty)
            return string.Join(Or, expr.Any.Select(Phrase));
        return expr.All.IsDefaultOrEmpty ? "" : string.Join(And, expr.All.Select(Phrase));
    }

    private static string Leaf(string leaf) => leaf.Split(':') switch
    {
        [Objectives.DefeatAll] => "defeat every enemy",
        ["defeat", var tag] => $"defeat the {Spoken(tag)}",
        ["survive", var rounds] => $"survive {rounds} {(rounds == "1" ? "round" : "rounds")}",
        ["reach", var zone, var count] => $"get {count} {(count == "1" ? "hero" : "heroes")} to the {Spoken(zone)}",
        ["protect", var tag] => $"keep the {Spoken(tag)} alive",
        ["flag", var name] => Spoken(name),
        _ => Spoken(leaf),
    };

    private static string Spoken(string id) => id.Replace('_', ' ');
}
