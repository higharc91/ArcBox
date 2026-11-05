using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>All authored roaming groups in their real locations, through normal TV views; no save is written.</summary>
public static class MobsDemo
{
    public static IReadOnlyList<ThingsDemoStep> Steps(string root)
    {
        var art = ArtManifests.Load(root);
        var rules = RulesPack.Load([Path.Combine(root, "rules")], art);
        var basis = ThingsDemo.Steps()[0].View;
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        var party = classes.Select((cls, i) => new WalkerView("mob-review-" + cls, new[] { "Ana", "Bo", "Cy", "Dee" }[i], "characters.class_" + cls, true, i == 0)).ToImmutableArray();
        var steps = new List<ThingsDemoStep>();
        var epoch = 0;
        double clock = 0;
        foreach (var (region, groups) in rules.Mobs)
        {
            var def = rules.RegionFor(region)!.Def;
            var ground = WorldGround.Of(WorldRegion.From(def), rules.Maps, art);
            var firstInRegion = steps.Count;
            foreach (var mob in groups)
            {
                epoch++;
                var at = mob.Patch.SelectMany(t => Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9).Select(y => new Tile(t.X + x, t.Y + y))))
                    .Distinct().Where(t => ground.Map.InBounds(t) && ground.Terrain.Walkable(t) && mob.Patch.All(p => Math.Abs(p.X - t.X) + Math.Abs(p.Y - t.Y) >= 3))
                    .OrderBy(t => mob.Patch.Min(p => Math.Abs(p.X - t.X) + Math.Abs(p.Y - t.Y))).ThenBy(t => t).First();
                var actors = mob.Show.Select((sprite, i) => new MobView(region + ":" + mob.Id + ":" + i, mob.Id, sprite, mob.Patch[i], 0)).ToImmutableArray();
                var explore = basis.Explore! with
                {
                    Region = region, Name = def.Name, Epoch = epoch, Seq = 1, At = at, Facing = mob.Patch[0], Party = party,
                    Leader = party[0].Name, Legs = [], Mobs = actors, Danger = rules.Encounters[mob.Encounter].Name,
                    Things = [.. rules.InteractablesIn(region).Where(t => t.Hidden is null && t.Id != "hollow_ember_claw")
                        .Select(t => ThingView.Of(t, Interactables.Initial(t), new HashSet<string>(["ch1_ford_settled", "sq_ashmaw_accepted", "sq_ashmaw_entered"])))],
                    PeopleFlags = ["ch1_ford_settled", "sq_ashmaw_accepted", "sq_ashmaw_entered"],
                    ThingEvents = null, Cast = [], Markers = [], Plugs = null, Traders = null, QuestMarkers = null,
                    QuestEvent = null, QuestLog = null, Pinned = null,
                };
                var view = basis with { Phase = nameof(TacticsPhase.Exploration), Explore = explore, Battle = null, Story = null, Notice = null };
                steps.Add(new(mob.Id, clock, view, [(14, mob.Id)], false));
                var occupied = actors.Select(a => a.At).ToHashSet();
                var walking = actors.Select(a =>
                {
                    var next = mob.Patch.FirstOrDefault(t => !occupied.Contains(t) && Math.Abs(t.X - a.At.X) + Math.Abs(t.Y - a.At.Y) == 1, a.At);
                    occupied.Remove(a.At); occupied.Add(next);
                    return a with { At = next, Step = 1, Legs = [new WalkLeg(1, next, 700)] };
                }).ToImmutableArray();
                steps.Add(new(mob.Id + "-walking", clock + 18, view with { Explore = explore with { Mobs = walking } }, [(.4, mob.Id + "-walking")], false));
                clock += 24;
            }
            // Revisit the first group after this region has warmed, for cold software renderers.
            var first = steps[firstInRegion];
            steps.Add(first with { Label = first.Label + "-settled", At = clock, Shots = [(3, first.Label + "-settled")] });
            clock += 8;
        }
        return steps;
    }
}
