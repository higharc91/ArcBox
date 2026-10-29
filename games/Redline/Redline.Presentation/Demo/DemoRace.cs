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
/// screenshotted without a server. Driver Select (claims arriving) → Qualifying (three laps of Reading, Question and Result:
/// one human lap deleted in lap 1, pole changing hands in lap 2, a player's purple lap in lap 3, the last classification being
/// the grid) → Grid → round 4's Throttle, Reading, Question and Reveal (a
/// human Redline spin-out, a three-car pack at the front, a pair behind) → Chequered → the podium: Anthem (the three
/// players shaking their bottles) and Champagne (sprays from all three steps at different strengths, one triple spray).
/// The winner is JO in the McLaren (Norris: God Save the King); <c>Steps(winner: "PER")</c> crowns KAI in the Cadillac
/// instead (Pérez: Mexico, the REDLINE theme only). Seven humans among the 22 cars,
/// one with a 12-character name; everyone else is AI. Distances follow the rules' payouts (<see cref="RedlineRules"/>).
/// <see cref="RedlineHostView.DeadlineUtc"/> is left null: the feeder stamps it when it plays a step.
/// </summary>
public static class DemoRace
{
    public const string Attribution = "Questions: Open Trivia DB (CC BY-SA 4.0)";
    public const int DemoRound = 4;

    /// <summary>The default race winner: a player whose driver's anthem has a melody (Norris, Great Britain).</summary>
    public const string DefaultWinner = "NOR";

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

    /// <summary>The qualifying laps the demo plays.</summary>
    public const int QualifyingLaps = 3;

    // Qualifying's easy questions, one per lap, with the correct answer's index.
    private static readonly (QuestionCard Card, int Correct)[] QualifyingQuestions =
    [
        (new("Geography", Difficulty.Easy, "What is the capital of France?", ["Berlin", "Paris", "Madrid", "Rome"]), 1),
        (new("Science", Difficulty.Easy, "How many legs does a spider have?", ["Six", "Ten", "Eight", "Four"]), 2),
        (new("General Knowledge", Difficulty.Easy, "Which colour do you get by mixing blue and yellow?", ["Green", "Orange", "Purple", "Brown"]), 0),
    ];

    // Qualifying order for the grid.
    private static readonly string[] GridOrder =
        ["NOR", "PIA", "LEC", "VER", "RUS", "HAM", "ANT", "ALO", "PER", "HAD", "GAS", "ALB", "SAI", "LAW", "HUL", "OCO", "BEA", "LIN", "COL", "STR", "BOR", "BOT"];

    /// <summary>
    /// Each car's three lap times in milliseconds, null for a deleted lap, so that the classification after lap 3 is
    /// <see cref="GridOrder"/>: correct laps first, then the total. PIA leads after lap 1 (and has that lap's purple), JO takes
    /// pole in lap 2 with the purple, ALEX has lap 3's purple; Christabella's first lap is deleted, as are three AI laps.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int?[]> LapTimes = BuildLapTimes();

    private static Dictionary<string, int?[]> BuildLapTimes()
    {
        var laps = new Dictionary<string, int?[]>(StringComparer.Ordinal)
        {
            ["PIA"] = [2300, 2500, 2600],
            ["NOR"] = [2450, 2200, 2400],
            ["LEC"] = [2700, 2600, 2150],
            ["HUL"] = [null, 3000, 2900],       // three correct laps would put Christabella ahead of the 14 cars above her
            ["OCO"] = [3100, null, 2950],
            ["BEA"] = [3100, 3000, null],
            ["LIN"] = [3100, 3050, null],
            ["COL"] = [null, 3100, 3100],
            ["STR"] = [3000, null, null],
            ["BOR"] = [3200, null, null],
            ["BOT"] = [null, null, null],
        };
        // Everyone else is a steady three correct laps, slower the further down the grid.
        for (var i = 0; i < GridOrder.Length; i++)
            if (!laps.ContainsKey(GridOrder[i]))
                laps[GridOrder[i]] = [2700 + 40 * i, 2650 + 40 * i, 2600 + 40 * i];
        return laps;
    }

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
    private static readonly Dictionary<string, int> FinalDefault = new(StringComparer.Ordinal)
    {
        ["NOR"] = 29, ["LEC"] = 28, ["ANT"] = 27, ["PIA"] = 27, ["RUS"] = 26, ["VER"] = 25, ["PER"] = 24, ["HAM"] = 24,
        ["GAS"] = 23, ["SAI"] = 22, ["HUL"] = 21, ["ALO"] = 21, ["HAD"] = 20, ["BEA"] = 19, ["LAW"] = 18, ["ALB"] = 17,
        ["BOR"] = 16, ["STR"] = 15, ["LIN"] = 15, ["OCO"] = 13, ["COL"] = 12, ["BOT"] = 11,
    };

    /// <summary>The whole script in play order.</summary>
    /// <param name="winner">The driver code of the race winner: one of the <see cref="Humans"/>.</param>
    public static IReadOnlyList<DemoStep> Steps(RedlineRules? rules = null, string winner = DefaultWinner)
    {
        rules ??= new RedlineRules();
        if (!Humans.ContainsKey(winner))
            throw new ArgumentException($"The demo winner must be a player's car ({string.Join(", ", Humans.Keys)}).", nameof(winner));
        // The winner swaps finishing distances with the default winner.
        var final = new Dictionary<string, int>(FinalDefault, StringComparer.Ordinal)
        {
            [DefaultWinner] = FinalDefault[winner],
            [winner] = FinalDefault[DefaultWinner],
        };
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

        steps.AddRange(QualifyingSteps(rules, grid));

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

        // Reading (audio spec §2): the question and every throttle on show while the question is read out.
        var reading = ordered.Select(p => Row(grid.ByCode(p.Code), true, before[p.Code], 0, p.Before, p.Before,
            DriverStatus.Ready) with { Throttle = p.Throttle }).ToList();
        var readingSeconds = Math.Ceiling(rules.ReadingTime(Card.Text!).TotalSeconds * 10) / 10;
        steps.Add(new DemoStep(View(rules, RacePhase.Reading, DemoRound, Card, null, reading), readingSeconds) { Label = "reading" });

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
        var finalRank = Rank(FinalDefault.Keys.Select(c => (c, final[c])));
        var classification = FinalDefault.Keys.OrderBy(c => finalRank[c])
            .Select(c => Row(grid.ByCode(c), true, finalRank[c], 0, final[c], final[c], DriverStatus.Ready)).ToList();
        var awards = new FinishSummary(
        [
            new Award(Ranking.BoldestDriver, Player("ANT"), Humans["ANT"], "5 Redlines"),
            new Award(Ranking.SharpestMind, Player("LEC"), Humans["LEC"], "10 of 11 right"),
            new Award(Ranking.BiggestCrash, Player("VER"), Humans["VER"], "2 spin-outs"),
        ]);
        steps.Add(new DemoStep(View(rules, RacePhase.Chequered, 11, null, null, classification) with { Finish = awards }, 8) { Label = "chequered" });

        // The podium: the three best-placed players, step 1 first.
        var podium = classification.Where(d => d.Player is not null).Take(3).ToList();
        var country = Countries.For(grid.ByCode(podium[0].Code).Driver.Nationality);
        var sprays = new List<Spray>[] { [], [], [] };
        var seq = 0;
        Podium Ceremony(double c1, double c2, double c3, params int[] fire)
        {
            double[] charges = [c1, c2, c3];
            foreach (var step in fire)
            {
                sprays[step - 1].Add(new Spray(++seq, Celebration.Strength(charges[step - 1])));
            }
            return new Podium(country.Name, country.AnthemId, [.. podium.Select((d, i) => new PodiumStep(
                i + 1, d.Player!.Value, d.PlayerName!, d.Code, d.TeamId, d.Position,
                fire.Contains(i + 1) ? 0 : Celebration.Quantise(charges[i]), [.. sprays[i]]))]);
        }
        DemoStep Stage(RacePhase phase, Podium p, double seconds) =>
            new(View(rules, phase, 11, null, null, classification) with { Podium = p }, seconds) { Label = phase.ToString().ToLowerInvariant() };

        // Anthem: everyone shakes; the bottles fill at different rates.
        steps.Add(Stage(RacePhase.Anthem, Ceremony(0, 0, 0), 3));
        steps.Add(Stage(RacePhase.Anthem, Ceremony(0.2, 0.1, 0.15), 3));
        steps.Add(Stage(RacePhase.Anthem, Ceremony(0.45, 0.3, 0.25), 3));
        steps.Add(Stage(RacePhase.Anthem, Ceremony(0.75, 0.5, 0.35), 3));
        steps.Add(Stage(RacePhase.Anthem, Ceremony(1.0, 0.7, 0.4), 4));

        // Champagne: the winner pops first at full pressure, then P2 and P3; all three at once; a second round.
        steps.Add(Stage(RacePhase.Champagne, Ceremony(1.0, 0.7, 0.4), 1.4));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(1.0, 0.7, 0.4, 1), 1.6));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.1, 0.7, 0.4, 2), 0.8));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.15, 0.05, 0.4, 3), 1.2));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.6, 0.5, 0.85), 0.8));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.6, 0.5, 0.85, 1, 3, 2), 1.0));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.3, 0.2, 0.15), 2.2));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(1.0, 0.95, 0.3), 1.0));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(1.0, 0.95, 0.3, 1, 2), 1.5));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.1, 0.1, 0.3, 3), 1.5));
        steps.Add(Stage(RacePhase.Champagne, Ceremony(0.2, 0.2, 0.2), 3.0));
        return steps;

        DriverRow Row(Seat s, bool showHuman, int position, int change, int distance, int previous, DriverStatus status)
        {
            var human = showHuman && Humans.TryGetValue(s.Driver.Code, out _);
            return new DriverRow(s.Driver.Code, s.Driver.Number, s.Team.Id,
                human ? Player(s.Driver.Code) : null, human ? Humans[s.Driver.Code] : null,
                position, change, distance, previous, status, null, null, null, false, true);
        }
    }

    /// <summary>
    /// The qualifying session: per lap a Reading (the question read out), a Question (some players still answering) and a
    /// Result (times, deleted laps, the purple lap, rows moving). Every view carries the classification as it stands.
    /// Nothing is shown before its Result, as the server guarantees.
    /// </summary>
    private static IEnumerable<DemoStep> QualifyingSteps(RedlineRules rules, Grid grid)
    {
        var seats = grid.Seats;
        // Players still answering in the Question view; every AI car counts as answered from the start.
        string[] slow = ["ALO", "HUL"];

        // The classification after <done> laps: correct laps, then total, then the seeded (team) order.
        List<(string Code, int Correct, int Total)> Board(int done) =>
        [.. seats.Select((s, tie) =>
            {
                var times = LapTimes[s.Driver.Code].Take(done).OfType<int>().ToList();
                return (Code: s.Driver.Code, Tie: tie, Correct: times.Count, Total: times.Sum());
            })
            .OrderByDescending(c => c.Correct).ThenBy(c => c.Total).ThenBy(c => c.Tie)
            .Select(c => (c.Code, c.Correct, c.Total))];

        RedlineHostView ViewOf(RacePhase phase, int lap, int question, IReadOnlyList<QualifyingRow> rows) =>
            View(rules, phase, 0, QualifyingQuestions[question].Card, phase == RacePhase.QualifyingResult ? QualifyingQuestions[question].Correct : null,
                [.. rows.Select(r => Driver(r.Code, r.Position, phase == RacePhase.QualifyingQuestion && r.Answered ? DriverStatus.Answered
                    : phase == RacePhase.QualifyingQuestion ? DriverStatus.Answering : DriverStatus.Ready))])
            with { Qualifying = new QualifyingSession(lap, QualifyingLaps, rows) };

        DriverRow Driver(string code, int position, DriverStatus status)
        {
            var seat = grid.ByCode(code);
            var human = Humans.TryGetValue(code, out var name);
            return new DriverRow(code, seat.Driver.Number, seat.Team.Id, human ? Player(code) : null, human ? name : null,
                position, 0, 0, 0, status, null, null, null, false, true);
        }

        for (var lap = 1; lap <= QualifyingLaps; lap++)
        {
            var before = Board(lap - 1);
            var after = Board(lap);
            var standing = before.Select((c, i) => new QualifyingRow(c.Code, i + 1, 0, c.Correct, c.Total, null, false, false)).ToList();
            var asked = standing.Select(r => r with { Answered = !Humans.ContainsKey(r.Code) || !slow.Contains(r.Code) }).ToList();

            var readingSeconds = Math.Ceiling(rules.ReadingTime(QualifyingQuestions[lap - 1].Card.Text!).TotalSeconds * 10) / 10;
            yield return new DemoStep(ViewOf(RacePhase.QualifyingReading, lap, lap - 1, standing), readingSeconds) { Label = "qualifying" };
            yield return new DemoStep(ViewOf(RacePhase.QualifyingQuestion, lap, lap - 1, asked), 4) { Label = "qualifying" };

            // The lap's fastest correct time; the better classified keeps a tie.
            var fastest = after.Select(c => (c.Code, Ms: LapTimes[c.Code][lap - 1])).Where(x => x.Ms is not null)
                .OrderBy(x => x.Ms).Select(x => x.Code).FirstOrDefault();
            var result = after.Select((c, i) => new QualifyingRow(c.Code, i + 1, before.FindIndex(b => b.Code == c.Code) - i,
                c.Correct, c.Total, LapTimes[c.Code][lap - 1], false, c.Code == fastest)).ToList();
            yield return new DemoStep(ViewOf(RacePhase.QualifyingResult, lap, lap - 1, result), rules.QualifyingResult.TotalSeconds) { Label = "qualifying" };
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
