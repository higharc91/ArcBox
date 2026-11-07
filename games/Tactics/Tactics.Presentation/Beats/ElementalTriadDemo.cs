using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only three-elemental review through the normal TV beat/FX path.</summary>
public static class ElementalTriadDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var battle = DemoBattle.View("river_crossing:elemental-triad").Battle!;
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
        void Move(double at, string who, Tile[] path, Tile fighter)
            => Add(at, who + " moving", new(who), [new Moved(new(who), [.. path])], fighter, (.3, who.ToLowerInvariant() + "-walk"));
        void Strike(double at, string who, Tile fighter)
            => Add(at, who + " natural strike", new(who),
                [new AbilityUsed(new(who), new("common.attack"), new Target(Unit: new("Fighter")), rules.Abilities[new("common.attack")].Presentation),
                    new AttackResolved(new(who), new("Fighter"), true, false), new Damaged(new("Fighter"), 4, who == "Fire" ? "fire" : who == "Air" ? "thunder" : "bludgeoning", 24)],
                fighter, (.25, who.ToLowerInvariant() + "-attack-windup"), (.48, who.ToLowerInvariant() + "-attack-contact"));
        void Special(double at, string who, string ability, string name, Tile fighter, double impact)
            => Add(at, name, new(who), [new AbilityUsed(new(who), new(ability), new Target(Unit: new(who)), rules.Abilities[new(ability)].Presentation)],
                fighter, (.2, name + "-windup"), (impact + .05, name + "-impact"));
        Add(0, "Three elemental forms", null, [], new(12,10), (.5, "elemental-ready"));
        Move(2, "Air", [new(9,10),new(10,10),new(9,10)], new(12,10));
        Move(4, "Earth", [new(12,9),new(11,9),new(12,9)], new(12,10));
        Move(6, "Fire", [new(13,6),new(13,7),new(13,6)], new(12,10));
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(7.2, "Target ready", null, [], new(10,10));
        Strike(8, "Air", new(10,10));
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(9.7, "Target ready", null, [], new(12,10));
        Strike(10.5, "Earth", new(12,10));
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(12.2, "Target ready", null, [], new(13,7));
        Strike(13, "Fire", new(13,7));
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(14.7, "Target ready", null, [], new(10,10));
        Special(15.5, "Air", "air_elemental.whirlwind", "air-whirlwind", new(10,10), .4);
        Special(18, "Air", "air_elemental.gale_blast", "air-gale-blast", new(10,10), .4);
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(19.7, "Target ready", null, [], new(12,10));
        Special(20.5, "Earth", "earth_elemental.tremor", "earth-tremor", new(12,10), .5);
        Special(23, "Earth", "golem.stone_aegis", "earth-aegis", new(12,10), .4);
        // Establish the review target before the beat so its previous/current snapshots agree.
        Add(24.7, "Target ready", null, [], new(13,7));
        Special(25.5, "Fire", "fire_elemental.flame_surge", "fire-surge", new(13,7), .4);
        Special(28, "Fire", "drake.flame_breath", "fire-breath", new(13,7), .4);
        Add(31, "Elementals ready again", null, [], new(12,10), (.5, "elemental-restored"));
        return steps;
    }
}
