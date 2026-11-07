using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only bites and hand-cast hellfire through the normal TV beat path.</summary>
public static class NightCompanionsDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:night-companions").Battle!;
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
            steps.Add(new ThingsDemoStep(label, at, new TacticsHostView("Battle", label, null, 1, [], null, battle, null, null), [.. shots], false));
        }
        void Move(double at, string who, Tile[] path)
            => Add(at, who + " moving", new(who), [new Moved(new(who), [.. path])], new(12,10), (.3, who.ToLowerInvariant() + "-walk"));
        void Strike(double at, string who, Tile fighter)
            => Add(at, who + (who == "Violet" ? " hellfire bolt" : " bite"), new(who),
                [new AbilityUsed(new(who), new("common.attack"), new Target(Unit: new("Fighter")), rules.Abilities[new("common.attack")].Presentation),
                    new AttackResolved(new(who), new("Fighter"), true, false), new Damaged(new("Fighter"), 4, who == "Violet" ? "fire" : "piercing", 24)],
                fighter, (.22, who.ToLowerInvariant() + "-attack-windup"), (.46, who.ToLowerInvariant() + "-attack-release"), (1.2, who.ToLowerInvariant() + "-recovery"));
        Add(0, "Three small library companions", null, [], new(12,10), (.5, "companion-ready"));
        Move(2, "Black", [new(9,10),new(10,10),new(9,10)]);
        Move(4, "Grey", [new(12,9),new(11,9),new(12,9)]);
        Move(6, "Violet", [new(13,6),new(13,7),new(13,6)]);
        Add(7.2, "Target ready", null, [], new(10,10));
        Strike(8, "Black", new(10,10));
        Add(10.2, "Target ready", null, [], new(12,10));
        Strike(11, "Grey", new(12,10));
        Add(13.2, "Target ready", null, [], new(12,10));
        Strike(14, "Violet", new(12,10));
        Add(18, "Companions ready again", null, [], new(12,10), (.5, "companion-restored"));
        return steps;
    }
}
