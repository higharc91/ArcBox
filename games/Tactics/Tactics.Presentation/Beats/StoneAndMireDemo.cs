using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Dev-only giant special-move review through the normal TV beat/FX path.</summary>
public static class StoneAndMireDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:stone-and-mire").Battle!;
        var steps = new List<ThingsDemoStep>();
        var seq = 0;
        void Add(double at, string label, UnitId? source, string? ability, UnitId? target,
            params (double After, string Name)[] shots)
        {
            if (source is { } by && ability is { } id)
            {
                var used = new AbilityUsed(by, new(id), new Target(Unit: target), rules.Abilities[new(id)].Presentation);
                battle = battle with { Beats = [new Beat(++seq, by, [used])] };
            }
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        Add(0, "Giant special-move review", null, null, null, (0.5, "giants-ready"));
        Add(2, "Troll: boulder toss", new("Troll"), "troll.boulder_toss", new("Fighter reference"),
            (0.30, "troll-boulder-held"), (0.47, "troll-boulder-release"));
        Add(5, "Troll: frenzy roar", new("Troll"), "troll.frenzy_roar", new("Troll"), (0.45, "troll-frenzy-roar"));
        Add(8, "Golem: seismic slam", new("Golem"), "golem.seismic_slam", new("Golem"),
            (0.34, "golem-slam-windup"), (0.57, "golem-slam-impact"));
        Add(11, "Golem: stone aegis", new("Golem"), "golem.stone_aegis", new("Golem"), (0.45, "golem-stone-aegis"));
        return steps;
    }
}
