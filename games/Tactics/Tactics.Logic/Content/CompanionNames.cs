using System.Globalization;
using System.Text;

namespace Tactics.Logic;

/// <summary>Server-owned naming rules for the three persistent subclass companions.</summary>
public static class CompanionNames
{
    public static (string? Name, string? Problem) Normalize(string value)
    {
        if (value.Any(char.IsControl)) return (null, "Names can be 1–16 letters, spaces, apostrophes or hyphens.");
        var name = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Normalize();
        if (name.Length == 0) return (null, null);
        if (new StringInfo(name).LengthInTextElements > 16 || name.EnumerateRunes().Any(r =>
            !Rune.IsLetter(r) && Rune.GetUnicodeCategory(r) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            && r.Value is not (' ' or '\'' or '-')) || !name.EnumerateRunes().Any(Rune.IsLetter))
            return (null, "Names can be 1–16 letters, spaces, apostrophes or hyphens.");
        return (name, null);
    }

    public static string? Monster(CharacterRecord c, RulesPack rules)
        => rules.Classes.GetValueOrDefault(c.Class)?.Subclasses.FirstOrDefault(s => s.Id == c.Subclass)?.Companion;
    public static string[] Suggestions(string monster) => monster switch
    {
        "hound" => ["Ash", "Ember", "Brindle", "Cinder", "Flint", "Bramble", "Rowan", "Coal"],
        "sapling" => ["Sprig", "Moss", "Hazel", "Alder", "Briar", "Willow", "Acorn", "Fern"],
        "imp" => ["Soot", "Wick", "Scorch", "Flicker", "Tinder", "Smudge", "Spark", "Crisp"],
        _ => [],
    };
}
