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
            var ability = new AbilityId(c.Sprite == "bosses.mimic.ironbound" ? "mimic.adhesive_bite" : "common.attack");
            Add(7.2 + i * 3, "Target ready", null, [], targets[i]);
            Add(8 + i * 3, c.Name + " natural attack", new(c.Id),
                [new AbilityUsed(new(c.Id), ability, new Target(Unit: new("Fighter")), rules.Abilities[ability].Presentation),
                    new AttackResolved(new(c.Id), new("Fighter"), true, false),
                    new Damaged(new("Fighter"), 4, c.Sprite switch
                    {
                        "beasts.bat.vampire" or "beasts.spider.phase" or "beasts.wolf.alpha"
                            or "bestiary.scorpion.black" or "bestiary.warg.rider" or "bosses.mimic.ironbound"
                            or "bosses.dragon.black" or "bosses.dragon.blue" or "bosses.dragon.green" or "bosses.dragon.gold" => "piercing",
                        "beasts.bear.undead" or "bosses.troll.frost" or "bosses.troll.ogre" => "slashing",
                        _ => "bludgeoning",
                    }, 24)],
                targets[i], (.22, c.Id.ToLowerInvariant() + "-attack-windup"),
                (c.Sprite.StartsWith("bosses.dragon.", StringComparison.Ordinal) ? .56 : .46, c.Id.ToLowerInvariant() + "-attack-contact"),
                (1.2, c.Id.ToLowerInvariant() + "-recovery"));
        }
        var next = 18d;
        for (var i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            if (c.Specials is not { Count: > 0 } specials) continue;
            var specialTarget = c.Sprite.StartsWith("bosses.troll.", StringComparison.Ordinal) ? new Tile(10, 10) : targets[i];
            Add(next - .8, c.Name + " special target ready", null, [], specialTarget);
            foreach (var special in specials)
            {
                var ability = new AbilityId(special.Ability);
                var name = c.Id.ToLowerInvariant() + "-" + special.Label;
                Add(next, name, new(c.Id),
                    [new AbilityUsed(new(c.Id), ability, new Target(Unit: new(special.Targeted ? "Fighter" : c.Id)),
                        rules.Abilities[ability].Presentation)], specialTarget,
                    (.2, name + "-windup"), (special.Impact + .05, name + "-impact"));
                next += 3;
            }
        }
        Add(next, "Library creatures ready again", null, [], new(12, 10), (.5, "library-restored"));
        return steps;
    }
}
