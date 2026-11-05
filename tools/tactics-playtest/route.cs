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

if (args.Length is not (5 or 6) || (args.Length == 6 && args[2] is not ("beat" or "shop" or "thing" or "person" or "mob" or "at")))
{
    Console.Error.WriteLine("usage: route.cs <rules dir> <region> <map> <x> <y>  |  route.cs <rules dir> <region> beat <beat id> <x> <y>");
    return 2;
}
var rules = RulesPack.Load([args[0]], ArtManifests.Load(Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0])))!.FullName));
var region = rules.RegionFor(args[1]) ?? throw new ArgumentException($"No region {args[1]}.");
// These runs start fresh campaigns. Include the interactables in their initial states, just as WalkGround does on the server:
// a town well or closed chest must not be treated as a walkable route tile merely because the underlying ground is clear.
var terrain = rules.GroundFor(region.Def.Id)!
    .With(Interactables.Blocking(rules.InteractablesIn(region.Def.Id), null, new HashSet<string>(StringComparer.Ordinal)));
var start = new Tile(int.Parse(args[^2]), int.Parse(args[^1]));

Func<Tile, bool> goal;
Func<Tile, bool>? avoid = null;
if (args.Length == 6 && args[2] == "at")
{
    var xy = args[3].Split(',').Select(int.Parse).ToArray();
    var target = new Tile(xy[0], xy[1]);
    goal = t => MobMotion.Contact(t, target);
}
else if (args.Length == 6 && args[2] == "mob")
{
    var mob = rules.MobsIn(region.Def.Id).Single(m => m.Id == args[3]);
    goal = t => mob.Patch.Any(p => MobMotion.Contact(t, p));
}
else if (args.Length == 6 && args[2] == "thing")
{
    var target = rules.InteractablesIn(region.Def.Id).Single(t => t.Id == args[3]);
    goal = t => Interactables.InReach(terrain, t, target);
}
else if (args.Length == 6 && args[2] == "person")
{
    var person = region.Def.People.Single(p => p.Id == args[3]);
    var target = new InteractableDef("person:" + person.Id, "sign", person.At);
    goal = t => Interactables.InReach(terrain, t, target);
}
else if (args.Length == 6 && args[2] == "shop")
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

// Story/shop routes avoid roaming danger; explicit mob/coordinate targets deliberately make contact.
if (!(args.Length == 6 && args[2] is "mob" or "at") && !args[2].StartsWith("mob_", StringComparison.Ordinal))
{
    var danger = rules.MobsIn(region.Def.Id).SelectMany(m => m.Patch).SelectMany(t => new[]
        { t, new Tile(t.X + 1, t.Y), new Tile(t.X - 1, t.Y), new Tile(t.X, t.Y + 1), new Tile(t.X, t.Y - 1) }).ToHashSet();
    var previous = avoid;
    avoid = t => danger.Contains(t) || previous?.Invoke(t) == true;
}
var route = WorldWalk.Route(terrain, start, goal, ClassDef.DefaultJump, avoid);
if (route is not { } taps)
{
    Console.WriteLine("none");
    return 1;
}
Console.WriteLine(taps.IsEmpty ? "here" : string.Join(' ', taps.Select(d => d.ToString().ToLowerInvariant())));
return 0;
