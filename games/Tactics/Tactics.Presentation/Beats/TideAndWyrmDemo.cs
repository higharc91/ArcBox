using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only final two-creature review through the normal TV beat/FX path.</summary>
public static class TideAndWyrmDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:tide-and-wyrm").Battle!;
        var steps = new List<ThingsDemoStep>();
        var seq = 0;
        void Add(double at, string label, UnitId? source, BattleEvent[] events, Tile fighter,
            params (double After, string Name)[] shots)
        {
            battle = battle with
            {
                Units = [.. battle.Units.Select(u => u.Id.Value == "Fighter" ? u with { At = fighter } : u)],
                Beats = events.Length == 0 ? [] : [new Beat(++seq, source, [.. events])],
            };
            var view = new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null);
            steps.Add(new ThingsDemoStep(label, at, view, [.. shots], false));
        }
        void Move(double at, string who, Tile[] path)
            => Add(at, who + " moving", new(who), [new Moved(new(who), [.. path])], new(12,10), (.3, who.ToLowerInvariant() + "-walk"));
        void Strike(double at, string who, Tile fighter)
            => Add(at, who + " natural strike", new(who),
                [new AbilityUsed(new(who), new("common.attack"), new Target(Unit: new("Fighter")), rules.Abilities[new("common.attack")].Presentation),
                    new AttackResolved(new(who), new("Fighter"), true, false), new Damaged(new("Fighter"), 4, who == "Dragon" ? "piercing" : "bludgeoning", 24)],
                fighter, (.25, who.ToLowerInvariant() + "-attack-windup"), (who == "Dragon" ? .56 : .46, who.ToLowerInvariant() + "-attack-contact"));
        void Special(double at, string who, string ability, string name, Tile fighter, double impact, bool targeted = false)
            => Add(at, name, new(who), [new AbilityUsed(new(who), new(ability), new Target(Unit: new(targeted ? "Fighter" : who)), rules.Abilities[new(ability)].Presentation)],
                fighter, (.2, name + "-windup"), (impact + .05, name + "-impact"));
        Add(0, "Tide and wyrm", null, [], new(12,10), (.5, "tide-and-wyrm-ready"));
        Move(2, "Water", [new(9,10),new(10,10),new(9,10)]);
        Move(4, "Dragon", [new(13,6),new(13,7),new(13,6)]);
        // Establish target locations before each beat so previous/current snapshots agree.
        Add(5.8, "Water target ready", null, [], new(10,10));
        Strike(6.6, "Water", new(10,10));
        Add(8.4, "Dragon target ready", null, [], new(13,8));
        Strike(9.2, "Dragon", new(13,8));
        Add(11, "Water special target ready", null, [], new(10,10));
        Special(12, "Water", "water_elemental.tidal_crush", "water-tidal-crush", new(10,10), .4);
        Special(14, "Water", "water_elemental.undertow", "water-undertow", new(10,10), .4, true);
        Add(16, "Dragon special target ready", null, [], new(13,8));
        Special(17, "Dragon", "dragon.fire_breath", "dragon-breath", new(13,8), .5);
        Special(20, "Dragon", "dragon.frightful_roar", "dragon-roar", new(13,8), .5);
        Special(23, "Dragon", "dragon.cataclysm", "dragon-wingbeat", new(13,8), .7);
        Add(26, "Tide and wyrm ready again", null, [], new(12,10), (.5, "tide-and-wyrm-restored"));
        return steps;
    }
}
