using System.Globalization;
using ArcBox.Sdk.Content;
using Meridian.Logic;
using Meridian.Sim;

// dotnet run --project games/Meridian/Meridian.Sim -- explore [--games N] [--players 3-6] [--seed S]
//     [--threshold T] [--faces 0,0,1,1,1,2] [--content path]
if (args.Length == 0 || args[0] is not ("explore" or "endgame"))
{
    Console.Error.WriteLine("Usage: Meridian.Sim explore|endgame [--games N] [--players 3-6] [--seed S] [--threshold T] [--faces a,b,...] [--scenario id|all] [--content path]");
    return 2;
}

var options = new Dictionary<string, string>();
for (var i = 1; i + 1 < args.Length; i += 2)
{
    options[args[i].TrimStart('-')] = args[i + 1];
}

var content = options.GetValueOrDefault("content") ?? FindContent();
var pack = PackLoader.Load(PhysicalFileSource.Instance, content);
var rules = pack.Rules;
if (options.TryGetValue("threshold", out var threshold))
{
    rules = rules with { BreachThreshold = int.Parse(threshold, CultureInfo.InvariantCulture) };
}
if (options.TryGetValue("faces", out var faces))
{
    rules = rules with { DieFaces = faces.Split(',').Select(f => int.Parse(f, CultureInfo.InvariantCulture)).ToEqArray() };
}
pack = pack.WithRules(rules);

var players = (options.GetValueOrDefault("players") ?? "3-6").Split('-');
var run = new ExploreOptions(
    Games: int.Parse(options.GetValueOrDefault("games") ?? "2000", CultureInfo.InvariantCulture),
    MinPlayers: int.Parse(players[0], CultureInfo.InvariantCulture),
    MaxPlayers: int.Parse(players[^1], CultureInfo.InvariantCulture),
    Seed: ulong.Parse(options.GetValueOrDefault("seed") ?? "1", CultureInfo.InvariantCulture));

if (args[0] == "endgame")
{
    var which = options.GetValueOrDefault("scenario") ?? "all";
    var ids = which == "all" ? pack.Breach.Scenarios.Items.ToList() : [which];
    var all = ids.SelectMany(id => EndgameReport.Run(pack, id, run.Games, run.MinPlayers, run.MaxPlayers, run.Seed)).ToList();
    Console.Write(EndgameReport.Format(all));
    return 0;
}

Console.Write(ExploreReport.Format(pack, ExploreReport.Run(pack, run)));
return 0;

static string FindContent()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
    {
        dir = dir.Parent;
    }
    return Path.Combine(dir?.FullName ?? ".", "content", "meridian", "base");
}
