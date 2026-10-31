using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Hud;

/// <summary>
/// A battle's objectives in words for the TV: "Defeat the warlord, or survive 11 rounds". The win expression is worded, with
/// any clock in the lose expression after it: the rest of a lose is the party falling, which goes without saying.
/// </summary>
public static class ObjectiveWords
{
    private const string Or = ", or ";
    private const string And = ", and ";

    /// <summary>The sentence for <paramref name="objectives"/>, or null if they say nothing. <paramref name="units"/> gives the villains their names.</summary>
    public static string? Of(ObjectivesDef? objectives, IEnumerable<UnitView>? units = null)
    {
        var words = objectives is null ? "" : Phrase(objectives.Win, [.. units ?? []]);
        if (words.Length == 0)
            return null;
        return char.ToUpperInvariant(words[0]) + words[1..] + (Clock(objectives!.Lose) is { } rounds ? $" within {rounds} rounds" : "");
    }

    private static string? Clock(ObjectiveExpr? lose)
    {
        if (lose is null)
            return null;
        ObjectiveExpr[] leaves = lose.Any.IsDefaultOrEmpty ? [lose] : [.. lose.Any];
        return leaves.Select(e => e.Leaf?.Split(':')).FirstOrDefault(l => l is ["survive", _])?[1];
    }

    private static string Phrase(ObjectiveExpr expr, UnitView[] units)
    {
        if (expr.Leaf is { } leaf)
            return Leaf(leaf, units);
        if (!expr.Any.IsDefaultOrEmpty)
            return string.Join(Or, expr.Any.Select(e => Phrase(e, units)));
        return expr.All.IsDefaultOrEmpty ? "" : string.Join(And, expr.All.Select(e => Phrase(e, units)));
    }

    /// <summary>A named villain's tag is its id and part of its name: "the warlord", a role, is neither of a Goblin Boss's.</summary>
    private static string Foe(string tag, UnitView[] units)
    {
        var named = units.FirstOrDefault(u => u.Id.Value == tag && tag.Split('_').All(word => u.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        return named?.Name ?? $"the {Spoken(tag)}";
    }

    private static string Leaf(string leaf, UnitView[] units) => leaf.Split(':') switch
    {
        [Objectives.DefeatAll] => "defeat every enemy",
        ["defeat", var tag] => $"defeat {Foe(tag, units)}",
        ["survive", var rounds] => $"survive {rounds} {(rounds == "1" ? "round" : "rounds")}",
        ["reach", var zone, var count] => $"get {count} {(count == "1" ? "hero" : "heroes")} to the {Spoken(zone)}",
        ["protect", var tag] => $"keep the {Spoken(tag)} alive",
        ["flag", var name] => Spoken(name),
        _ => Spoken(leaf),
    };

    private static string Spoken(string id) => id.Replace('_', ' ');
}
