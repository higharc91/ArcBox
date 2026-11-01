#:project ../../games/Tactics/Tactics.Logic/Tactics.Logic.csproj
// The way into a stamp, for bots.cjs --walk-to-stamp: the pad directions that walk the party from a tile into the stamp of a battle map,
// by the server's own rule for walking (WorldWalk, at the default Jump), so the route can't disagree with the party.
//
//   dotnet run tools/tactics-playtest/route.cs -- <rules dir> <region> <map> <x> <y>
//
// Prints one line: the directions, one a step and space-separated (e, ne, ...), the last of them the step into the stamp; or
// `none` if the stamp can't be walked into from there. Every step is one WorldWalk.Next takes as asked, never a slide round a wall,
// so it doesn't depend on which way the party last went. Only the last step enters the stamp: walking inside one doesn't start a fight.
using Tactics.Logic;

if (args.Length != 5)
{
    Console.Error.WriteLine("usage: route.cs <rules dir> <region> <map> <x> <y>");
    return 2;
}
var rules = RulesPack.Load([args[0]], ArtManifests.Load(Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0])))!.FullName));
var region = rules.RegionFor(args[1]) ?? throw new ArgumentException($"No region {args[1]}.");
var map = rules.MapFor(args[2]) ?? throw new ArgumentException($"No map {args[2]}.");
var stage = BattleStage.Of(region, map) ?? throw new ArgumentException($"{args[1]} doesn't stamp {args[2]}.");
var terrain = rules.GroundFor(region.Def.Id)!.Terrain;
var start = new Tile(int.Parse(args[3]), int.Parse(args[4]));

var came = new Dictionary<Tile, (Tile From, WalkDir Dir)>();
var open = new Queue<Tile>([start]);
var seen = new HashSet<Tile> { start };
Tile? end = null;
while (end is null && open.TryDequeue(out var at))
    foreach (var dir in Enum.GetValues<WalkDir>())
    {
        var (dx, dy) = WorldWalk.Delta(dir);
        if (WorldWalk.Next(terrain, at, dir, ClassDef.DefaultJump, null) is not { Slid: false } move
            || move.To != new Tile(at.X + dx, at.Y + dy) || !seen.Add(move.To))
            continue;
        came[move.To] = (at, dir);
        if (stage.Holds(move.To))
        {
            end = move.To;
            break;
        }
        open.Enqueue(move.To);
    }

if (end is not { } found)
{
    Console.WriteLine("none");
    return 1;
}
var dirs = new List<string>();
for (var t = found; t != start; t = came[t].From)
    dirs.Add(came[t].Dir.ToString().ToLowerInvariant());
dirs.Reverse();
Console.WriteLine(string.Join(' ', dirs));
return 0;
