using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only companion gait and bite review through the normal TV beat path.</summary>
public static class TrailCompanionDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps()
    {
        var battle = DemoBattle.View("river_crossing:trail-companion").Battle!;
        var steps = new List<ThingsDemoStep>();
        var hound = new UnitId("Hound");
        var seq = 0;
        void Add(double at, string label, BattleEvent[] events, params (double After, string Name)[] shots)
        {
            battle = battle with { Beats = events.Length == 0 ? [] : [new Beat(++seq, hound, [.. events])] };
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        Add(0, "Watchful companion", [], (0.5, "hound-idle"));
        Add(2, "Four-legged gait", [new Moved(hound, [new(12, 10), new(12, 9), new(11, 9), new(12, 9), new(12, 10)])],
            (0.35, "hound-walk"));
        Add(4, "Natural bite", [new AbilityUsed(hound, new("common.attack"), new Target(Unit: new("Bandit")), null)],
            (0.22, "hound-bite-windup"), (0.5, "hound-bite-contact"), (1, "hound-recovery"));
        Add(7, "Ready again", [], (0.5, "hound-ready"));
        return steps;
    }
}
