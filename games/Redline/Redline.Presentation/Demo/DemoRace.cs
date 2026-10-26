using ArcBox.Sdk;
using Redline.Logic;

namespace Redline.Presentation.Demo;

/// <summary>One scripted host view and how many seconds the demo holds it before the next.</summary>
public sealed record DemoStep(RedlineHostView View, double Seconds)
{
    /// <summary>Short label for screenshots and logs, e.g. "reveal" or "driverselect-2".</summary>
    public required string Label { get; init; }
}

/// <summary>
/// The dev-only <c>--redline-demo</c> script: a realistic race told in host views, so the TV scene can be run and
/// screenshotted without a server. Driver Select (claims arriving) → Grid → round 4's Throttle, Question and Reveal (a
/// human Redline spin-out, a three-car pack at the front, a pair behind) → Chequered. Seven humans among the 22 cars,
/// one with a 12-character name; everyone else is AI. Distances follow the rules' payouts (<see cref="RedlineRules"/>).
/// <see cref="RedlineHostView.DeadlineUtc"/> is left null: the feeder stamps it when it plays a step.
/// </summary>
public static class DemoRace
{
    public const string Attribution = "Questions: Open Trivia DB (CC BY-SA 4.0)";
    public const int DemoRound = 4;

    /// <summary>Human seats: driver code → player name.</summary>
    public static readonly IReadOnlyDictionary<string, string> Humans = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LEC"] = "ALEX",
        ["NOR"] = "JO",
        ["VER"] = "SAM",
        ["ANT"] = "RIA",
        ["ALO"] = "MO",
        ["PER"] = "KAI",
        ["HUL"] = "Christabella",
    };

    private static readonly QuestionCard Card = new("Geography", Difficulty.Medium,
        "Which U.S. state has the longest coastline?", ["Florida", "California", "Alaska", "Hawaii"]);
    private const int CorrectIndex = 2;

    // Qualifying order for the grid.
    private static readonly string[] GridOrder =
        ["NOR", "PIA", "LEC", "VER", "RUS", "HAM", "ANT", "ALO", "PER", "HAD", "GAS", "ALB", "SAI", "LAW", "HUL", "OCO", "BEA", "LIN", "COL", "STR", "BOR", "BOT"];

    private sealed record Plan(string Code, int Before, Throttle Throttle, bool Right);

    // Distances after three rounds, then round 4's throttle and answer for every car.
    private static readonly Plan[] Round4 =
    [
        new("NOR", 10, Throttle.Cruise, true),
        new("LEC", 9, Throttle.Push, true),
        new("VER", 9, Throttle.Redline, false),
        new("PIA", 9, Throttle.Push, true),
        new("RUS", 8, Throttle.Redline, true),
        new("HAM", 8, Throttle.Cruise, true),
        new("ANT", 7, Throttle.Redline, true),
        new("ALO", 7, Throttle.Push, false),
        new("PER", 6, Throttle.Push, true),
        new("HAD", 6, Throttle.Cruise, true),
        new("GAS", 6, Throttle.Push, true),
        new("ALB", 5, Throttle.Cruise, false),
        new("SAI", 5, Throttle.Push, true),
        new("LAW", 5, Throttle.Cruise, true),
        new("HUL", 4, Throttle.Push, true),
        new("OCO", 4, Throttle.Redline, false),
        new("BEA", 4, Throttle.Push, true),
        new("LIN", 3, Throttle.Cruise, true),
        new("COL", 3, Throttle.Push, false),
        new("STR", 3, Throttle.Cruise, true),
        new("BOR", 2, Throttle.Push, true),
        new("BOT", 1, Throttle.Cruise, true),
    ];

    // Where the race ends: the leader over the line.
    private static readonly Dictionary<string, int> Final = new(StringComparer.Ordinal)
    {
        ["LEC"] = 29, ["NOR"] = 28, ["ANT"] = 27, ["PIA"] = 27, ["RUS"] = 26, ["VER"] = 25, ["PER"] = 24, ["HAM"] = 24,
        ["GAS"] = 23, ["SAI"] = 22, ["HUL"] = 21, ["ALO"] = 21, ["HAD"] = 20, ["BEA"] = 19, ["LAW"] = 18, ["ALB"] = 17,
        ["BOR"] = 16, ["STR"] = 15, ["LIN"] = 15, ["OCO"] = 13, ["COL"] = 12, ["BOT"] = 11,
    };

    /// <summary>The whole script in play order.</summary>
    public static IReadOnlyList<DemoStep> Steps(RedlineRules? rules = null)
    {
        rules ??= new RedlineRules();
        var grid = Grid2026.Load();
        var seats = grid.Seats;
        var steps = new List<DemoStep>();

        // Driver Select: team order, claims arriving.
        string[][] claims = [[], ["LEC"], ["LEC", "NOR", "VER"], [.. Humans.Keys]];
        for (var i = 0; i < claims.Length; i++)
        {
            var claimed = claims[i].ToHashSet(StringComparer.Ordinal);
            var rows = seats.Select((s, index) => Row(s, claimed.Contains(s.Driver.Code), index + 1, 0, 0, 0,
                claimed.Contains(s.Driver.Code) ? DriverStatus.Seated : DriverStatus.Unclaimed)).ToList();
            steps.Add(new DemoStep(View(rules, RacePhase.DriverSelect, 0, null, null, rows), i == 0 ? 2 : 3) { Label = "driverselect" });
        }

        // Grid: everyone at distance 0 in qualifying order.
        var gridRows = GridOrder.Select((code, i) => Row(grid.ByCode(code), true, i + 1, 0, 0, 0, DriverStatus.Ready)).ToList();
        steps.Add(new DemoStep(View(rules, RacePhase.Grid, 1, null, null, gridRows), 5) { Label = "grid" });

        // Round 4.
        var before = Rank(Round4.Select(p => (p.Code, p.Before)));
        var after = Rank(Round4.Select(p => (p.Code, Math.Max(0, p.Before + Gain(rules, p)))));
        var ordered = Round4.OrderBy(p => before[p.Code]).ThenBy(p => Array.FindIndex(Round4, x => x.Code == p.Code)).ToList();
        var throttleCard = Card with { Text = null, Answers = null };

        var throttle = ordered.Select((p, i) => Row(grid.ByCode(p.Code), true, before[p.Code], 0, p.Before, p.Before,
            i % 4 == 3 ? DriverStatus.Choosing : DriverStatus.Locked)).ToList();
        steps.Add(new DemoStep(View(rules, RacePhase.Throttle, DemoRound, throttleCard, null, throttle), 4) { Label = "throttle" });

        var question = ordered.Select((p, i) => Row(grid.ByCode(p.Code), true, before[p.Code], 0, p.Before, p.Before,
            i % 3 == 1 ? DriverStatus.Answering : DriverStatus.Answered) with { Throttle = p.Throttle }).ToList();
        steps.Add(new DemoStep(View(rules, RacePhase.Question, DemoRound, Card, null, question), 5) { Label = "question" });

        var reveal = Round4
            .OrderBy(p => after[p.Code]).ThenBy(p => before[p.Code])
            .Select(p =>
            {
                var distance = Math.Max(0, p.Before + Gain(rules, p));
                var outcome = p.Right ? RoundOutcome.Correct : p.Throttle == Throttle.Redline ? RoundOutcome.SpunOut : RoundOutcome.Wrong;
                return Row(grid.ByCode(p.Code), true, after[p.Code], before[p.Code] - after[p.Code], distance, p.Before, DriverStatus.Ready) with
                {
                    Throttle = p.Throttle,
                    AnswerIndex = p.Right ? CorrectIndex : (CorrectIndex + 1 + p.Code.Length % 3) % 4,
                    Outcome = outcome,
                    PitLimiter = outcome == RoundOutcome.SpunOut,
                };
            }).ToList();
        steps.Add(new DemoStep(View(rules, RacePhase.Reveal, DemoRound, Card, CorrectIndex, reveal), 7) { Label = "reveal" });

        // Chequered.
        var finalRank = Rank(Final.Select(kv => (kv.Key, kv.Value)));
        var classification = Final.Keys.OrderBy(c => finalRank[c])
            .Select(c => Row(grid.ByCode(c), true, finalRank[c], 0, Final[c], Final[c], DriverStatus.Ready)).ToList();
        var awards = new FinishSummary(
        [
            new Award(Ranking.BoldestDriver, Player("ANT"), Humans["ANT"], "5 Redlines"),
            new Award(Ranking.SharpestMind, Player("LEC"), Humans["LEC"], "10 of 11 right"),
            new Award(Ranking.BiggestCrash, Player("VER"), Humans["VER"], "2 spin-outs"),
        ]);
        steps.Add(new DemoStep(View(rules, RacePhase.Chequered, 11, null, null, classification) with { Finish = awards }, 8) { Label = "chequered" });
        return steps;

        DriverRow Row(Seat s, bool showHuman, int position, int change, int distance, int previous, DriverStatus status)
        {
            var human = showHuman && Humans.TryGetValue(s.Driver.Code, out _);
            return new DriverRow(s.Driver.Code, s.Driver.Number, s.Team.Id,
                human ? Player(s.Driver.Code) : null, human ? Humans[s.Driver.Code] : null,
                position, change, distance, previous, status, null, null, null, false, true);
        }
    }

    private static PlayerId Player(string code) => new($"demo-{code.ToLowerInvariant()}");

    private static int Gain(RedlineRules rules, Plan p) => (p.Throttle, p.Right) switch
    {
        (Throttle.Cruise, true) => rules.CruiseGain,
        (Throttle.Cruise, false) => 0,
        (Throttle.Push, true) => rules.PushGain,
        (Throttle.Push, false) => -rules.PushLoss,
        (Throttle.Redline, true) => rules.RedlineGain,
        _ => -rules.RedlineLoss,
    };

    /// <summary>Distance desc, ties share a position (1, 2, 2, 4) in input order.</summary>
    private static Dictionary<string, int> Rank(IEnumerable<(string Code, int Distance)> field)
    {
        var sorted = field.OrderByDescending(x => x.Distance).ToList();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sorted.Count; i++)
            result[sorted[i].Code] = i > 0 && sorted[i].Distance == sorted[i - 1].Distance ? result[sorted[i - 1].Code] : i + 1;
        return result;
    }

    private static RedlineHostView View(RedlineRules rules, RacePhase phase, int round, QuestionCard? card, int? correct, IReadOnlyList<DriverRow> rows) =>
        new(phase, round, rules.MaxRounds, rules.FinishLine, rules.CircuitId, card, correct, rows, null, null, Attribution, Payouts.From(rules));
}
