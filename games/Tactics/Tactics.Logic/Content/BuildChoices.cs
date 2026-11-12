using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Additional explicit character-creation and level-up choices, with legacy-save defaults.</summary>
public static class BuildChoices
{
    public static readonly ImmutableSortedSet<string> Ancestries = Ordinal.Set("acid", "cold", "fire", "lightning", "poison");
    public static IEnumerable<string> Feats(RulesPack rules) => rules.Passives.Keys.Where(id => id.StartsWith("feat.", StringComparison.Ordinal));
    public static int SpentFeats(CharacterRecord c) => c.Feats.IsDefault ? 0 : c.Feats.Length;

    internal static void Validate(CharacterRecord c, RaceDef? race, ClassDef? cls, BackgroundDef? background, RulesPack rules, ContentProblems problems)
    {
        if (!c.Feats.IsDefaultOrEmpty)
        {
            var due = c.Level >= 8 ? 4 : c.Level >= 4 ? 2 : 0;
            if (c.Increases.Values.Sum() + c.Feats.Length * 2 > due) problems.Add("/feats", "Feats share the level 4 and 8 ability-increase choices.");
            if (c.Feats.Distinct(StringComparer.Ordinal).Count() != c.Feats.Length) problems.Add("/feats", "Choose each feat only once.");
            foreach (var feat in c.Feats)
                if (string.IsNullOrEmpty(feat) || !feat.StartsWith("feat.", StringComparison.Ordinal) || !rules.Passives.ContainsKey(feat)) problems.Add("/feats", $"Unknown feat '{feat}'.");
        }
        if (!c.ClassSkills.IsDefault && cls is not null && race is not null && background is not null)
        {
            var count = cls.SkillChoices.Count + race.ExtraSkills;
            if (c.ClassSkills.Length != count || c.ClassSkills.Distinct(StringComparer.Ordinal).Count() != count)
                problems.Add("/classSkills", $"Choose {count} different class skills.");
            if (c.ClassSkills.Any(s => !cls.SkillChoices.From.Contains(s) || background.Skills.Contains(s)))
                problems.Add("/classSkills", "Choose skills from your class that your background does not already grant.");
        }
        if (c.DragonAncestry is { } ancestry && (c.Race != "dragonborn" || !Ancestries.Contains(ancestry)))
            problems.Add("/dragonAncestry", "Choose a dragonborn ancestry: acid, cold, fire, lightning or poison.");
    }

    public static AbilityId Breath(string ancestry) => new(ancestry == "fire" ? "dragonborn.breath_weapon" : "dragonborn.breath_weapon_" + ancestry);
}
