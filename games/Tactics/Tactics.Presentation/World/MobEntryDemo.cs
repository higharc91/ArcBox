using Tactics.Logic;
using Tactics.Presentation.Board;
namespace Tactics.Presentation.World;

/// <summary>Native renderer review of each authored mob entry through real encounter setup.</summary>
public static class MobEntryDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string root)
    {
        var rules = RulesPack.Load([Path.Combine(root, "rules")]);
        var steps = new List<ThingsDemoStep>();
        foreach (var groups in rules.Mobs.Values)
        foreach (var mob in groups)
        foreach (var entry in rules.MapFor(mob.Arena)!.Zones.Keys.Where(k => k.StartsWith(MobSpawns.Prefix, StringComparison.Ordinal)))
        {
            var name = mob.Id + "-" + entry[MobSpawns.Prefix.Length..];
            var view = DemoBattle.Encounter(mob.Encounter, root, entry);
            view = view with { Battle = view.Battle! with { Name = rules.Encounters[mob.Encounter].Name + " · " + entry[MobSpawns.Prefix.Length..] + " entry" } };
            steps.Add(new(name, steps.Count * 4, view, [(2, name)], false));
        }
        return steps;
    }
}
