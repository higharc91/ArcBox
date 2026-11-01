using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Tactics.Logic;
using Tactics.Sim;

const string Usage = """
    Tactics.Sim: the Tactics balance simulator (spec §5.6). Plays every encounter with sampled AI parties, AI on both sides,
    and writes the Markdown balance report.

    Usage:
      dotnet run -c Release --project games/Tactics/Tactics.Sim -- [options]

    Options:
      --encounters a,b,...   encounters to play (default: every encounter in the pack)
      --levels 1,3,...       party levels (default 1,3,5,7,10)
      --sizes 4,5,6          party sizes (default 4,5,6)
      --parties N            sampled parties per size (default 6)
      --seeds N              seeds 1..N per encounter, level and party (default 20)
      --difficulty D         Story, Normal or Hard (default Normal)
      --round-cap N          stop a battle after this round, counted as lost (default 30)
      --rules DIR            the rules pack (default content/tactics/base/rules under the repository root)
      --out PATH             write the report to PATH (default: print it)
      -h, --help             this text
    """;

ImmutableArray<string>? encounters = null;
ImmutableArray<int> levels = [1, 3, 5, 7, 10];
ImmutableArray<int> sizes = [4, 5, 6];
var parties = 6;
var seeds = 20;
var difficulty = Difficulty.Normal;
var roundCap = 30;
string? rulesDir = null;
string? outPath = null;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
        int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
        ImmutableArray<int> Ints() => [.. Next().Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];

        switch (args[i])
        {
            case "-h" or "--help": Console.WriteLine(Usage); return 0;
            case "--encounters": encounters = [.. Next().Split(',')]; break;
            case "--levels": levels = Ints(); break;
            case "--sizes": sizes = Ints(); break;
            case "--parties": parties = Int(); break;
            case "--seeds": seeds = Int(); break;
            case "--difficulty": difficulty = Enum.Parse<Difficulty>(Next(), ignoreCase: true); break;
            case "--round-cap": roundCap = Int(); break;
            case "--rules": rulesDir = Next(); break;
            case "--out": outPath = Next(); break;
            default: throw new ArgumentException($"Unknown option '{args[i]}'.");
        }
    }
}
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

rulesDir ??= Path.Combine(RepoRoot(), "content", "tactics", "base", "rules");
var rules = RulesPack.Load([rulesDir]);
var config = new SimConfig(encounters ?? [.. rules.Encounters.Keys], levels, parties, sizes, seeds, difficulty, roundCap);

var watch = Stopwatch.StartNew();
var battles = new List<SimBattle>();
foreach (var battle in SimRunner.Run(config, rules))
{
    battles.Add(battle);
    if (battles.Count % 50 == 0)
        Console.Error.WriteLine($"{battles.Count} battles, {watch.Elapsed.TotalSeconds:0}s");
}
Console.Error.WriteLine($"{battles.Count} battles in {watch.Elapsed.TotalSeconds:0}s.");

var report = Report.Markdown(battles, config);
if (outPath is null)
    Console.Write(report);
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, report);
    Console.Error.WriteLine($"Wrote {outPath}.");
}
return 0;

// The folder holding ArcBox.slnx, above the working directory or the binaries.
static string RepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return dir.FullName;
    throw new DirectoryNotFoundException("Can't find the repository root (no ArcBox.slnx above here); pass --rules.");
}
