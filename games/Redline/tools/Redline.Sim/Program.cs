using System.Diagnostics;
using System.Globalization;
using System.Text;
using Redline.Logic;
using Redline.Sim;

const string Usage = """
    Redline.Sim: headless Monte-Carlo balance simulator for REDLINE.
    Drives the real RedlineGame through GameHarness; human seats are bots that read their phone view and send inputs.

    Usage:
      dotnet run --project games/Redline/tools/Redline.Sim -c Release -- [options]

    Options:
      -n, --races N            races per scenario (default 2000)
      --seed S                 master seed (default 2026)
      --only a,b,...           run only scenarios whose name starts with one of these prefixes
      --list                   list the scenarios and exit
      --mix E,M,H              question difficulty weights (default 1,1,1)
      --latency model|instant  human reaction times (default model: medians claim 6 s, throttle 3 s, answer 7 s)
      --seats random|top       humans claim a random free seat, or the free driver with the most 2026 points
      --out PATH               also write the Markdown report to PATH
      -h, --help               this text

    Rule overrides (for what-if runs; the game code is not changed, a different RedlineRules is passed in):
      --finish N  --max-rounds N  --cruise-gain N  --push-gain N  --push-loss N  --redline-gain N  --redline-loss N
      --ai-easy X  --ai-medium X  --ai-hard X  --ai-min-p X  --ai-max-p X  --ai-temp X  --ai-limiter-cost X
      --ai-redline-floor X  --ai-chase-gap N  --ai-chase-rounds N  --ai-chase-bonus X

    Examples:
      dotnet run --project games/Redline/tools/Redline.Sim -c Release -- -n 500 --only solo
      dotnet run --project games/Redline/tools/Redline.Sim -c Release -- --only mixed --redline-gain 3 --ai-medium 0.6
    """;

var races = 2000;
var seed = 2026;
string[]? only = null;
var list = false;
var mix = DifficultyMix.Even;
var latency = Latency.Model;
var seats = SeatPick.Random;
string? outPath = null;
var rules = new RedlineRules();
var ai = rules.Ai;

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
        int Int() => int.Parse(Next(), CultureInfo.InvariantCulture);
        double Dbl() => double.Parse(Next(), CultureInfo.InvariantCulture);

        switch (args[i])
        {
            case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
            case "-n" or "--races": races = Int(); break;
            case "--seed": seed = Int(); break;
            case "--only": only = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
            case "--list": list = true; break;
            case "--mix":
                var w = Next().Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                if (w.Length != 3) throw new ArgumentException("--mix needs three weights: easy,medium,hard.");
                mix = new DifficultyMix(w[0], w[1], w[2]);
                break;
            case "--latency":
                latency = Next() switch
                {
                    "model" => Latency.Model,
                    "instant" => Latency.Instant,
                    var x => throw new ArgumentException($"Unknown latency '{x}'."),
                };
                break;
            case "--seats":
                seats = Next() switch
                {
                    "random" => SeatPick.Random,
                    "top" => SeatPick.Top,
                    var x => throw new ArgumentException($"Unknown seat pick '{x}'."),
                };
                break;
            case "--out": outPath = Next(); break;
            case "--finish": rules = rules with { FinishLine = Int() }; break;
            case "--max-rounds": rules = rules with { MaxRounds = Int() }; break;
            case "--cruise-gain": rules = rules with { CruiseGain = Int() }; break;
            case "--push-gain": rules = rules with { PushGain = Int() }; break;
            case "--push-loss": rules = rules with { PushLoss = Int() }; break;
            case "--redline-gain": rules = rules with { RedlineGain = Int() }; break;
            case "--redline-loss": rules = rules with { RedlineLoss = Int() }; break;
            case "--ai-easy": ai = ai with { Easy = Dbl() }; break;
            case "--ai-medium": ai = ai with { Medium = Dbl() }; break;
            case "--ai-hard": ai = ai with { Hard = Dbl() }; break;
            case "--ai-min-p": ai = ai with { MinP = Dbl() }; break;
            case "--ai-max-p": ai = ai with { MaxP = Dbl() }; break;
            case "--ai-temp": ai = ai with { Temperature = Dbl() }; break;
            case "--ai-limiter-cost": ai = ai with { LimiterCost = Dbl() }; break;
            case "--ai-redline-floor": ai = ai with { RedlineFloor = Dbl() }; break;
            case "--ai-chase-gap": ai = ai with { ChaseGap = Int() }; break;
            case "--ai-chase-rounds": ai = ai with { ChaseRoundsLeft = Int() }; break;
            case "--ai-chase-bonus": ai = ai with { ChaseBonus = Dbl() }; break;
            default: throw new ArgumentException($"Unknown option '{args[i]}'.");
        }
    }
    if (races < 1) throw new ArgumentException("--races must be at least 1.");
}
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    return 2;
}

rules = rules with { Ai = ai };
var scenarios = Scenarios.Default()
    .Where(s => only is null || only.Any(p => s.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
    .ToList();

if (list)
{
    foreach (var s in Scenarios.Default()) Console.WriteLine($"{s.Name,-26} {s.Description}");
    return 0;
}
if (scenarios.Count == 0)
{
    Console.Error.WriteLine("No scenario matches --only.");
    return 2;
}

var options = new SimOptions(races, seed, rules, mix, latency, seats);
var clock = Stopwatch.StartNew();
var runs = new List<(Scenario, RaceRecord[])>();
foreach (var s in scenarios)
{
    var records = new RaceRecord[races];
    var t = Stopwatch.StartNew();
    Parallel.For(0, races, i => records[i] = RaceRunner.Run(s, i, options));
    Console.Error.WriteLine($"{s.Name,-26} {races} races in {t.Elapsed.TotalSeconds:F1} s");
    runs.Add((s, records));
}
clock.Stop();

var md = new StringBuilder()
    .Append(Report.Header(options, clock.Elapsed, races * scenarios.Count))
    .AppendLine("## Scenarios")
    .AppendLine()
    .AppendJoin(Environment.NewLine, scenarios.Select(s => $"- `{s.Name}`: {s.Description}"))
    .AppendLine().AppendLine()
    .Append(Report.Overview(runs, rules))
    .Append(Report.RoundHistogram(runs, rules))
    .Append(Report.Humans(runs))
    .Append(Report.HeadToHead(runs))
    .Append(Report.Ai(runs))
    .Append(Report.Timing(runs, rules))
    .ToString();

Console.WriteLine(md);
if (outPath is not null) File.WriteAllText(outPath, md);
return 0;
