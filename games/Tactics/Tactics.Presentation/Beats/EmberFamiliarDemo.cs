using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only wing gait and natural hellfire review through the normal TV beat path.</summary>
public static class EmberFamiliarDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:ember-familiar").Battle!;
        var steps = new List<ThingsDemoStep>();
        var imp = new UnitId("Imp");
        var target = new UnitId("Bandit");
        var seq = 0;
        void Add(double at, string label, BattleEvent[] events, params (double After, string Name)[] shots)
        {
            battle = battle with { Beats = events.Length == 0 ? [] : [new Beat(++seq, imp, [.. events])] };
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        Add(0, "Bound companion", [], (0.5, "imp-idle"));
        Add(2, "Wing-assisted gait", [new Moved(imp, [new(11, 9), new(12, 9), new(12, 10), new(12, 9), new(11, 9)])],
            (0.35, "imp-walk"));
        Add(4, "Natural hellfire bolt", [new AbilityUsed(imp, new("common.attack"), new Target(Unit: target),
            rules.Abilities[new("common.attack")].Presentation), new AttackResolved(imp, target, true, false), new Damaged(target, 4, "fire", 22)],
            (0.22, "imp-bolt-windup"), (0.46, "imp-bolt-flight"), (1.2, "imp-recovery"));
        Add(8, "Ready again", [], (0.5, "imp-ready"));
        return steps;
    }
}
