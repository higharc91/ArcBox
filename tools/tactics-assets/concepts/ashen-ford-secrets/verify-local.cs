#:project ../../../../games/Tactics/Tactics.WorldTools/Tactics.WorldTools.csproj
#:property PublishAot=false
using System.Text.Json;
using Tactics.Logic;
using Tactics.WorldTools;

var pack = Path.GetFullPath(args.Single());
var rules = RulesPack.Load([Path.Combine(pack, "rules")], ArtManifests.Load(pack));
var region = rules.RegionFor("ashen_ford")!;
var story = StoryOrder.Check(region, rules, pack);
var ground = rules.GroundFor("ashen_ford")!.Terrain;
var result = new {
    packLoaded = true, storyLegs = story.Legs.Length, recoveryLegs = story.Retries.Length,
    problems = story.Problems,
    allStoryLegsReachable = story.Legs.All(l => l.Seconds is not null),
    allRecoveryLegsReachable = story.Retries.All(l => l.Seconds is not null),
    granaryReviewStandWalkable = ground.Walkable(new Tile(38, 74)),
    maudReviewStandWalkable = ground.Walkable(new Tile(131, 91)),
    granaryMarkerWalkable = ground.Walkable(new Tile(42, 70)),
    maudMarkerWalkable = ground.Walkable(new Tile(132, 93))
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return story.Problems.IsEmpty && result.allStoryLegsReachable && result.allRecoveryLegsReachable &&
    result.granaryReviewStandWalkable && result.maudReviewStandWalkable &&
    result.granaryMarkerWalkable && result.maudMarkerWalkable ? 0 : 1;
