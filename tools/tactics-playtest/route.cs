#:project ../../games/Tactics/Tactics.Logic/Tactics.Logic.csproj
// The way to a fight or a beat, for bots.cjs: the pad directions that walk the party from a tile into the stamp of a battle map
// (--walk-to-stamp), or into reach of a beat's "!" (--chapter), by the server's own rule for walking (WorldWalk.Route, at the default
// Jump), so the route can't disagree with the party.
//
//   dotnet run tools/tactics-playtest/route.cs -- <rules dir> <region> <map> <x> <y>
//   dotnet run tools/tactics-playtest/route.cs -- <rules dir> <region> beat <beat id> <x> <y>
//
// Prints one line: the directions, one a step and space-separated (e, ne, ...); `here` if the party is in reach of the beat already;
// or `none` if there's no way. Every step is one WorldWalk.Next takes as asked, never a slide round a wall, so it doesn't depend on
// which way the party last went. Into a stamp, only the last step enters it: walking inside one doesn't start a fight. To a beat,
// the walk ends on the first tile from which Interact is offered (StoryRules.InReach), and goes round every stamp: a battle beat's
// fight starts from its "!", from the side the beat's place is on, never from wherever the shortest way first touches its ground
// (unless there's no way to the "!" but across one).
using Tactics.Logic;

if (args.Length is not (5 or 6) || (args.Length == 6 && args[2] is not ("beat" or "shop")))
{
    Console.Error.WriteLine("usage: route.cs <rules dir> <region> <map> <x> <y>  |  route.cs <rules dir> <region> beat <beat id> <x> <y>");
    return 2;
}
var rules = RulesPack.Load([args[0]], ArtManifests.Load(Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0])))!.FullName));
var region = rules.RegionFor(args[1]) ?? throw new ArgumentException($"No region {args[1]}.");
var terrain = rules.GroundFor(region.Def.Id)!.Terrain;
var start = new Tile(int.Parse(args[^2]), int.Parse(args[^1]));

Func<Tile, bool> goal;
Func<Tile, bool>? avoid = null;
if (args.Length == 6 && args[2] == "shop")
{
    var shop = rules.ShopsIn(region.Def.Id).Single(s => s.Id == args[3]);
    var at = shop.Keeper.Person is { } person ? region.Def.People[person].At : rules.InteractablesIn(region.Def.Id).Single(t => t.Id == shop.Keeper.Thing).At;
    var target = new InteractableDef(shop.Id, "stall", at);
    goal = t => Interactables.InReach(terrain, t, target);
}
else if (args.Length == 6)
{
    var spot = region.Def.Beats.SingleOrDefault(b => b.Beat == args[3])?.At ?? throw new ArgumentException($"{args[1]} has no spot for {args[3]}.");
    // Round the fights' ground, as a player walking to a "!" would: a step into an armed stamp starts its fight from wherever it is.
    // A "!" that can only be got to across a stamp (the far bank's, after a lost fight) is walked to across it.
    var stages = region.Def.Stamps.Select(st => rules.MapFor(st.Map) is { } m ? BattleStage.Of(region, m) : null).OfType<BattleStage>().ToList();
    var inStamp = (Tile t) => stages.Any(st => st.Holds(t));
    goal = t => !inStamp(t) && StoryRules.InReach(terrain, t, spot);
    if (WorldWalk.Route(terrain, start, goal, ClassDef.DefaultJump, inStamp) is not null)
        avoid = inStamp;
    else
        goal = t => StoryRules.InReach(terrain, t, spot);
}
else
{
    var map = rules.MapFor(args[2]) ?? throw new ArgumentException($"No map {args[2]}.");
    var stage = BattleStage.Of(region, map) ?? throw new ArgumentException($"{args[1]} doesn't stamp {args[2]}.");
    goal = stage.Holds;
}

var route = WorldWalk.Route(terrain, start, goal, ClassDef.DefaultJump, avoid);
if (route is not { } taps)
{
    Console.WriteLine("none");
    return 1;
}
Console.WriteLine(taps.IsEmpty ? "here" : string.Join(' ', taps.Select(d => d.ToString().ToLowerInvariant())));
return 0;
