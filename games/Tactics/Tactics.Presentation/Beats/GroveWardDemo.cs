using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only root gait and physical vine reach review through the normal TV beat path.</summary>
public static class GroveWardDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:grove-ward").Battle!;
        var steps = new List<ThingsDemoStep>();
        var sapling = new UnitId("Sapling");
        var target = new UnitId("Bandit");
        var seq = 0;
        void Add(double at, string label, BattleEvent[] events, params (double After, string Name)[] shots)
        {
            battle = battle with { Beats = events.Length == 0 ? [] : [new Beat(++seq, sapling, [.. events])] };
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        Add(0, "Grove companion", [], (0.5, "sapling-idle"));
        Add(2, "Root-footed gait", [new Moved(sapling, [new(11, 9), new(12, 9), new(12, 10), new(12, 9), new(11, 9)])],
            (0.35, "sapling-walk"));
        Add(4, "Physical vine lash", [new AbilityUsed(sapling, new("common.attack"), new Target(Unit: target),
            rules.Abilities[new("common.attack")].Presentation), new AttackResolved(sapling, target, true, false), new Damaged(target, 4, "bludgeoning", 22)],
            (0.22, "sapling-lash-windup"), (0.44, "sapling-lash-contact"), (1.2, "sapling-recovery"));
        Add(8, "Ready again", [], (0.5, "sapling-ready"));
        return steps;
    }
}
