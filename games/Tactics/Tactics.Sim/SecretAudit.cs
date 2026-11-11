using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>Actual default-sheet passive detection, independent of battle simulation.</summary>
public static class SecretAudit
{
    /// <summary>Write every hidden thing's eligible, in-range detection for default four-class parties at levels 1–3.</summary>
    public static void Write(RulesPack rules, string path)
    {
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        var rows = Enumerable.Range(1, 3).Select(level =>
        {
            var sheets = classes.Select(cls => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, level, rules), rules)).ToArray();
            var passives = HiddenDef.Skills.ToDictionary(skill => skill,
                skill => sheets.Max(sheet => Interactables.Passive(sheet, skill)));
            var regions = rules.Interactables.Select(region => new
            {
                region = region.Key,
                secrets = region.Value.Where(t => t.Hidden is not null).Select(t => new
                {
                    id = t.Id, dc = t.Hidden!.Dc, skill = t.Hidden.Skill, t.SearchOnly,
                    range = Interactables.RangeOf(t), requires = t.Requires, unless = t.Unless,
                    passive = passives[t.Hidden.Skill],
                    foundPassivelyWhenEligibleAndInRange = Interactables.Finds([t], null,
                        t.Requires.ToHashSet(StringComparer.Ordinal), t.At, skill => passives[skill]).Any(f => f.Def.Id == t.Id),
                    t.Reveals,
                }).ToArray(),
            }).ToArray();
            return new { level, classes, passives, regions };
        }).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}
