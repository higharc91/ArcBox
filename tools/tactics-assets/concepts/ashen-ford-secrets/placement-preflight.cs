#:project ../../../../games/Tactics/Tactics.WorldTools/Tactics.WorldTools.csproj
#:property PublishAot=false
using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Logic;
using Tactics.WorldTools;

var pack = Path.GetFullPath(args.FirstOrDefault() ?? "content/tactics/base");
var rules = RulesPack.Load([Path.Combine(pack, "rules")], ArtManifests.Load(pack));
var region = rules.RegionFor("ashen_ford")!;
if (!rules.Art!.Props["crate"].Blocks)
    throw new InvalidOperationException("Preflight needs a blocking placeholder.");
ImmutableArray<Tile> footprint = [.. Enumerable.Range(72, 2).SelectMany(y => Enumerable.Range(43, 3).Select(x => new Tile(x, y)))];
var placeholder = new RegionProps("crate", "oak", footprint);
var staged = WorldRegion.From(region.Def with { Props = region.Def.Props.Add(placeholder) });
var story = StoryOrder.Check(staged, rules, pack);
var result = new {
    status = story.Problems.IsEmpty ? "collision-only candidate passed" : "candidate failed",
    proposedGranaryAt = new[] {43, 72}, footprint = new[] {3, 2},
    replacement = "cottage_39 at [43,72]; remove overlapping clutter_barrels_41 at [45,73] for actual preview",
    storyLegs = story.Legs.Length, retryLegs = story.Retries.Length,
    allStoryLegsReachable = story.Legs.All(l => l.Seconds is not null),
    allRetriesReachable = story.Retries.All(l => l.Seconds is not null),
    problems = story.Problems,
    foundation = footprint.Select(t => new { x=t.X, y=t.Y, height=region.TileAt(t)!.Height,
        material=region.Def.Materials[region.TileAt(t)!.Material] }),
    caveat = "Read-only collision simulation over the current region. Art anchors, doorway orientation, roof occlusion and final placement still require generated pixels and TV review; production map was not changed."
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return story.Problems.IsEmpty && story.Legs.All(l => l.Seconds is not null) && story.Retries.All(l => l.Seconds is not null) ? 0 : 1;
