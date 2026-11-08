using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Dev-only oak Mimic motion review through the normal TV beat path.</summary>
public static class OakAmbushDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:oak-ambush").Battle!;
        var steps = new List<ThingsDemoStep>();
        var mimic = new UnitId("Mimic");
        var fighter = new UnitId("Fighter");
        var seq = 0;
        void Add(double at, string label, BattleEvent[] events, params (double After, string Name)[] shots)
        {
            battle = battle with { Beats = events.Length == 0 ? [] : [new Beat(++seq, mimic, [.. events])] };
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        Add(0, "The closed chest", [], (0.5, "mimic-disguise"));
        Add(2, "Short-footed skitter", [new Moved(mimic, [new(12, 9), new(13, 9), new(12, 9), new(13, 9), new(12, 9)])],
            (0.35, "mimic-skitter"));
        Add(4, "Natural bite", [new AbilityUsed(mimic, new("common.attack"), new Target(Unit: fighter), new("bosses.attack", null, null))],
            (0.22, "mimic-bite-windup"), (0.5, "mimic-bite-contact"));
        Add(7, "Adhesive bite", [new AbilityUsed(mimic, new("mimic.adhesive_bite"), new Target(Unit: fighter),
            rules.Abilities[new("mimic.adhesive_bite")].Presentation)], (0.5, "mimic-adhesive-contact"));
        Add(10, "Disguise restored", [], (0.5, "mimic-restored"));
        return steps;
    }
}
