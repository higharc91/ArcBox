using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;
namespace Tactics.Presentation.Beats;

/// <summary>Deterministic native UI fixtures, not a human or live-session playtest.</summary>
public static class RollPanelDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps()
    {
        var hero = new UnitId("hero1"); var foe = new UnitId("goblin.1");
        UnitView Unit(UnitId id, string name, string side, string sprite, Tile at) => new(id, name, side, sprite, at, 30, 30, 0, "Up", 0, [], null);
        var battle = new BattleView("river_crossing", "river_crossing", 1, [hero, foe], hero,
            [Unit(hero, "Ana", "Party", "characters.class_wizard", new(3, 3)), Unit(foe, "Goblin", "Enemy", "characters.goblin_cutthroat", new(4, 3))], [], null, BoardChanges.None);
        var steps = new List<ThingsDemoStep>();
        void Add(double at, string name, params BattleEvent[] events)
        {
            if (events.Length > 0)
            {
                foreach (var e in events) battle = DisplayedBattle.Apply(battle, e, battle);
                battle = battle with { Beats = [new Beat(steps.Count, hero, [.. events])] };
            }
            steps.Add(new(name, at, new TacticsHostView("Battle", name, null, 1, [], null, battle, null, null), [(1.3, name), (2.3, name + "-settled")], false));
        }
        Add(0, "roll-panel-ready");
        Add(4, "roll-panel-miss", new AbilityUsed(hero, new("common.attack"), new Target(Unit: foe), null),
            new Rolled(hero, RollKind.Attack, new([9], 9, 3, 12, 14, false, false, false)), new AttackResolved(hero, foe, false, false));
        Add(12, "roll-panel-save-push", new AbilityUsed(hero, new("wizard.burning_hands"), new Target(Unit: foe), null),
            new Rolled(foe, RollKind.Save, new([8], 8, 2, 10, 13, false, false, false)) { Context = new(hero, new("wizard.burning_hands")) },
            new ConditionApplied(foe, new("prone")), new Pushed(foe, new(4, 3), new(5, 3)));
        Add(20, "roll-panel-shield", new AbilityUsed(foe, new("common.attack"), new Target(Unit: hero), null),
            new Rolled(foe, RollKind.Attack, new([9], 9, 5, 14, 12, true, false, false)),
            new AbilityUsed(hero, new("wizard.shield"), new Target(Unit: hero), null), new ConditionApplied(hero, new("shielded")),
            new AttackResolved(foe, hero, false, false) { FinalAc = 17 });
        return steps;
    }
}
