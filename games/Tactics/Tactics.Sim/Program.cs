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
      --companions N         the first N members of every party (class order) play the companion profile (default 0)
      --trinkets cls:id,...  equip listed class trinkets for a paired reward-impact sweep (default none)
      --subclasses cls:id,...
                             give the listed classes that subclass (default none)
      --race ID              every party member's race (default human)
      --races cls:id,...     per-class race overrides for mixed parties
      --party a,b,c,d        an explicit party instead of sampled compositions
      --no-increases        controlled legacy comparison without level-4/8 stat increases
      --build-audit          paired sweep of all 36 subclasses and all races at levels 3/5/7/10
      --with-class CLS       play only the sampled parties that include this class
      --weapons cls:id,...   give the listed classes this weapon instead of their starting kit's
      --policy hidden-rogue  party rogues play the scripted rogue that hides whenever it may and strikes from hiding
      --rotate-formation    rotate spawn seats across seeds (default: fixed class order)
      --rules DIR            the rules pack (default content/tactics/base/rules under the repository root)
      --out PATH             write the report to PATH (default: print it)
      --secret-audit        write default-party secret detection at levels 1–3 to --out
      --json PATH            also write the complete config and battle measurements (gzip for .gz)
      -h, --help             this text
    """;

ImmutableArray<string>? encounters = null;
ImmutableArray<int> levels = [1, 3, 5, 7, 10];
ImmutableArray<int> sizes = [4, 5, 6];
var parties = 6;
var seeds = 20;
var difficulty = Difficulty.Normal;
var roundCap = 30;
var companions = 0;
ImmutableDictionary<string, string>? trinkets = null;
ImmutableDictionary<string, string>? subclasses = null;
string? race = null;
string? withClass = null;
ImmutableDictionary<string, string>? weapons = null;
string? policy = null;
var rotateFormation = false;
var increases = true;
var buildAudit = false;
var secretAudit = false;
ImmutableArray<string> fixedParty = default;
ImmutableDictionary<string, string>? races = null;
string? rulesDir = null;
string? outPath = null;
string? jsonPath = null;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
        int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
        ImmutableArray<int> Ints() => [.. Next().Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))];
        ImmutableDictionary<string, string> Pairs(string what) => Next().Split(',').Select(s => s.Split(':', 2)).ToImmutableDictionary(
            p => p.Length == 2 ? p[0] : throw new ArgumentException($"{what} use class:id pairs."), p => p[1], StringComparer.Ordinal);

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
            case "--companions": companions = Int(); break;
            case "--trinkets": trinkets = Pairs("Trinkets"); break;
            case "--subclasses": subclasses = Pairs("Subclasses"); break;
            case "--race": race = Next(); break;
            case "--races": races = Pairs("Races"); break;
            case "--party": fixedParty = [.. Next().Split(',')]; sizes = [fixedParty.Length]; break;
            case "--no-increases": increases = false; break;
            case "--secret-audit": secretAudit = true; break;
            case "--build-audit": buildAudit = true; break;
            case "--with-class": withClass = Next(); break;
            case "--weapons": weapons = Pairs("Weapons"); break;
            case "--policy": policy = Next(); break;
            case "--rotate-formation": rotateFormation = true; break;
            case "--rules": rulesDir = Next(); break;
            case "--out": outPath = Next(); break;
            case "--json": jsonPath = Next(); break;
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
if (secretAudit)
{
    SecretAudit.Write(rules, outPath ?? throw new ArgumentException("--secret-audit requires --out"));
    return 0;
}
var config = new SimConfig(encounters ?? [.. rules.Encounters.Keys], levels, parties, sizes, seeds, difficulty, roundCap, trinkets, companions,
    subclasses, race, withClass, weapons, policy, rotateFormation, increases, fixedParty, races);
if (buildAudit)
{
    if (outPath is null) throw new ArgumentException("--build-audit needs --out for its Markdown and raw JSON reports.");
    BuildAudit.Write(rules, seeds, roundCap, outPath);
    return 0;
}

var watch = Stopwatch.StartNew();
var battles = new List<SimBattle>();
foreach (var battle in SimRunner.Run(config, rules))
{
    battles.Add(battle);
    if (battles.Count % 50 == 0)
        Console.Error.WriteLine($"{battles.Count} battles, {watch.Elapsed.TotalSeconds:0}s");
}
Console.Error.WriteLine($"{battles.Count} battles in {watch.Elapsed.TotalSeconds:0}s.");

if (jsonPath is not null) RawReport.Write(jsonPath, config, battles);

var report = (config.Encounters.All(e => e.StartsWith("mob_", StringComparison.Ordinal)) ? MobReport.Markdown(battles) : "") + Report.Markdown(battles, config);
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
