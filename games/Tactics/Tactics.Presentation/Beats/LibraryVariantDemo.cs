using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Beats;

/// <summary>Development-only movement and natural attacks through the normal TV beat path.</summary>
public static class LibraryVariantDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string pack, string batch)
    {
        var rules = RulesPack.Load([Path.Combine(pack, "rules")]);
        var creatures = LibraryVariantReview.Creatures(batch);
        var battle = LibraryVariantReview.View("river_crossing", batch).Battle!;
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
            => Add(at, who + " moving", new(who), [new Moved(new(who), [.. path])], new(12, 10), (.3, who.ToLowerInvariant() + "-walk"));
        Add(0, "Library creatures ready", null, [], new(12, 10), (.5, "library-ready"));
        Move(2, creatures[0].Id, [new(9, 10), new(10, 10), new(9, 10)]);
        Move(4, creatures[1].Id, [new(12, 9), new(11, 9), new(12, 9)]);
        Move(6, creatures[2].Id, [new(13, 6), new(13, 7), new(13, 6)]);
        Tile[] targets = [new(10, 10), new(12, 10), new(13, 7)];
        for (var i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            Add(7.2 + i * 3, "Target ready", null, [], targets[i]);
            Add(8 + i * 3, c.Name + " natural attack", new(c.Id),
                [new AbilityUsed(new(c.Id), new("common.attack"), new Target(Unit: new("Fighter")), rules.Abilities[new("common.attack")].Presentation),
                    new AttackResolved(new(c.Id), new("Fighter"), true, false),
                    new Damaged(new("Fighter"), 4, c.Sprite switch { "beasts.bat.vampire" => "piercing", "beasts.bear.undead" => "slashing", _ => "bludgeoning" }, 24)],
                targets[i], (.22, c.Id.ToLowerInvariant() + "-attack-windup"), (.46, c.Id.ToLowerInvariant() + "-attack-contact"), (1.2, c.Id.ToLowerInvariant() + "-recovery"));
        }
        Add(18, "Library creatures ready again", null, [], new(12, 10), (.5, "library-restored"));
        return steps;
    }
}
