using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Sim;

public enum SeatPick { Random, Top }

/// <summary>Everything that stays the same across the races of one run.</summary>
public sealed record SimOptions(
    int Races, int Seed, RedlineRules Rules, DifficultyMix Mix, Latency Latency, SeatPick Seats, QualiModel Quali);

public sealed record Scenario(string Name, string Description, IReadOnlyList<BotSpec> Humans);

/// <summary>One car's line in the final classification.</summary>
/// <param name="Bot">Index into <see cref="Scenario.Humans"/>, or -1 for an AI car.</param>
public sealed record CarRecord(
    string Code, double AiSkill, int Bot, int Distance, int Position,
    int Correct, int Cruises, int Pushes, int Redlines, int SpinOuts, int NoAnswers,
    int DifficultyPoints,             // correct answers weighted easy 1 / medium 2 / hard 3 (a candidate tie-break)
    int GridSlot,                     // the starting position: qualifying's classification, or the seeded shuffle without it
    int QualiCorrect,                 // correct qualifying laps, 0..QualifyingLaps (0 when qualifying did not run)
    int QualiTotalMs);                // the sum of the correct laps' times

public sealed record RaceRecord(
    int Rounds,
    bool EndedByLine,
    double GameSeconds,
    CarRecord[] Cars,                 // final classification order
    string[] LeaderByRound,           // first P1 code after each Reveal
    bool[] AiLedRound,                // no human shared P1 after that Reveal
    int ThrottleTimeouts,
    int AnswerTimeouts,
    int ThrottleDecisions,
    int AnswerDecisions,
    int AiThrottleDecisions,          // AI cars choosing a throttle (not pit-limited)
    int AiChaseDecisions,             // ... of which with AiPolicy's chase condition met
    double ReadingSeconds,            // total length of the Reading phases (0 with ReadQuestions off)
    bool Qualified,                   // qualifying ran (on in the rules and at least one human)
    double QualifyingSeconds,         // harness time from the first QualifyingReading/Question to Grid
    double QualiReadingSeconds,       // ... of which QualifyingReading (never ends early)
    int LapTimeouts,                  // human qualifying answers drawn past the 10 s window
    int LapDecisions);                // human qualifying answers drawn

/// <summary>
/// Drives one real <see cref="RedlineGame"/> through <see cref="GameHarness{TGame}"/>. Human bots only read their own
/// <see cref="ControllerView"/> (plus the public TV view for distances, as a person in the room would) and send the
/// same JSON inputs a phone sends. Each bot action is scheduled at a drawn reaction time on the harness clock, so
/// early advance and timeouts happen exactly as the shipped code does them.
/// </summary>
public static class RaceRunner
{
    private sealed class Tally
    {
        public int Correct, Cruises, Pushes, Redlines, SpinOuts, NoAnswers, DifficultyPoints;
    }

    private enum Act { Claim, Throttle, Answer, Lap }

    public static RaceRecord Run(Scenario scenario, int raceIndex, SimOptions o)
    {
        var seed = Seeds.For(o.Seed, scenario.Name, raceIndex);
        var source = new SyntheticQuestionSource(new Random(seed ^ 0x5EED5EED), o.Mix);
        var game = new RedlineGame(source, o.Rules);
        var humans = scenario.Humans;
        var h = new GameHarness<RedlineGame>(game, humans.Count, seed);
        var botRandom = new Random(unchecked(seed * 31 + 7));
        // Qualifying draws come from their own stream, so the race bots' decisions are drawn exactly as without qualifying.
        var qualiRandom = new Random(unchecked(seed * 37 + 11));
        var limiterCost = humans.Select(b => BotBrain.LimiterCost(b.Skill, o.Mix, o.Rules)).ToArray();
        var botOf = new Dictionary<PlayerId, int>();
        for (var i = 0; i < humans.Count; i++) botOf[h.Players[i].Id] = i;

        var grid = Grid2026.Load();
        var skillOf = grid.Seats.ToDictionary(s => s.Driver.Code, s => s.Driver.AiSkill);
        var pointsOf = grid.Seats.ToDictionary(s => s.Driver.Code, s => s.Driver.Points);
        var tally = grid.Seats.ToDictionary(s => s.Driver.Code, _ => new Tally());

        var start = h.Now;
        h.Start();

        var leaders = new List<string>();
        var aiLed = new List<bool>();
        int throttleTimeouts = 0, answerTimeouts = 0, throttleDecisions = 0, answerDecisions = 0, aiDecisions = 0, aiChase = 0;
        var readingSeconds = 0.0;
        double qualiReadingSeconds = 0;
        int lapTimeouts = 0, lapDecisions = 0;
        DateTimeOffset? qualiStart = null, qualiEnd = null, lapOpenedAt = null;
        Dictionary<string, int>? gridSlot = null;
        Dictionary<string, (int Correct, int TotalMs)> qualiOf = [];
        CarRecord[]? final = null;
        var finalRound = 0;

        var pending = new List<(DateTimeOffset At, int Bot, Act Act)>();
        (RacePhase Phase, int Round, int Lap)? key = null;

        while (!h.IsOver)
        {
            var view = h.HostView<RedlineHostView>();
            if (key != (view.Phase, view.Round, view.Qualifying?.Lap ?? 0))
            {
                key = (view.Phase, view.Round, view.Qualifying?.Lap ?? 0);
                pending.Clear();
                OnPhaseStart(view);
            }

            var deadline = view.DeadlineUtc ?? throw new InvalidOperationException($"No phase timer in {view.Phase}.");
            pending.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Bot.CompareTo(b.Bot));
            if (pending.Count > 0 && pending[0].At < deadline)
            {
                var (at, bot, act) = pending[0];
                pending.RemoveAt(0);
                if (at > h.Now) h.Advance(at - h.Now);
                Perform(bot, act, view);
            }
            else
            {
                foreach (var (_, _, act) in pending)
                {
                    if (act == Act.Throttle) throttleTimeouts++;
                    else if (act == Act.Answer) answerTimeouts++;
                    else if (act == Act.Lap) lapTimeouts++;
                }
                pending.Clear();
                h.Advance(deadline - h.Now);
            }
        }

        return new RaceRecord(
            finalRound,
            final!.Any(c => c.Distance >= o.Rules.FinishLine),
            (h.Now - start).TotalSeconds,
            final!,
            [.. leaders],
            [.. aiLed],
            throttleTimeouts, answerTimeouts, throttleDecisions, answerDecisions, aiDecisions, aiChase, readingSeconds,
            qualiStart is not null,
            qualiStart is { } qs ? (qualiEnd!.Value - qs).TotalSeconds : 0,
            qualiReadingSeconds, lapTimeouts, lapDecisions);

        void OnPhaseStart(RedlineHostView view)
        {
            switch (view.Phase)
            {
                case RacePhase.DriverSelect:
                    for (var i = 0; i < humans.Count; i++)
                        pending.Add((h.Now + o.Latency.Draw(o.Latency.ClaimMedian, botRandom), i, Act.Claim));
                    break;
                case RacePhase.Throttle:
                {
                    // AiPolicy's chase condition, evaluated on the public distances.
                    var leaderDistance = view.Drivers.Max(r => r.Distance);
                    var roundsLeft = o.Rules.MaxRounds - view.Round + 1;
                    foreach (var row in view.Drivers.Where(r => r.Player is null && !r.PitLimiter))
                    {
                        aiDecisions++;
                        if (AiPolicy.IsChasing(leaderDistance - row.Distance, roundsLeft, o.Rules.Ai)) aiChase++;
                    }
                    for (var i = 0; i < humans.Count; i++)
                    {
                        if (h.PlayerView(i) is not ButtonsView) continue;     // pit limiter: nothing to choose
                        throttleDecisions++;
                        pending.Add((h.Now + o.Latency.Draw(o.Latency.ThrottleMedian, botRandom), i, Act.Throttle));
                    }
                    break;
                }
                case RacePhase.QualifyingReading:
                    // As in Reading: the question is on the phones, answers are not open yet, and the phase runs to its timer.
                    qualiStart ??= h.Now;
                    qualiReadingSeconds += (view.DeadlineUtc!.Value - h.Now).TotalSeconds;
                    break;
                case RacePhase.QualifyingQuestion:
                    qualiStart ??= h.Now;
                    lapOpenedAt = h.Now;
                    for (var i = 0; i < humans.Count; i++)
                    {
                        if (h.PlayerView(i) is not ChoiceView) continue;
                        lapDecisions++;
                        pending.Add((h.Now + o.Quali.Draw(qualiRandom), i, Act.Lap));
                    }
                    break;
                case RacePhase.QualifyingResult:
                    qualiOf = view.Qualifying!.Rows.ToDictionary(r => r.Code, r => (r.Correct, r.TotalMs));
                    break;
                case RacePhase.Grid:
                    if (qualiStart is not null) qualiEnd = h.Now;
                    gridSlot = view.Drivers.ToDictionary(r => r.Code, r => r.Position);
                    break;
                case RacePhase.Reading:
                    // The question is on the phones but answers are not open yet: bots wait, and the phase runs to its timer.
                    readingSeconds += (view.DeadlineUtc!.Value - h.Now).TotalSeconds;
                    break;
                case RacePhase.Question:
                    for (var i = 0; i < humans.Count; i++)
                    {
                        if (h.PlayerView(i) is not ChoiceView) continue;
                        answerDecisions++;
                        pending.Add((h.Now + o.Latency.Draw(o.Latency.AnswerMedian, botRandom), i, Act.Answer));
                    }
                    break;
                case RacePhase.Reveal:
                    foreach (var row in view.Drivers)
                    {
                        var t = tally[row.Code];
                        switch (row.Throttle)
                        {
                            case Throttle.Cruise: t.Cruises++; break;
                            case Throttle.Push: t.Pushes++; break;
                            case Throttle.Redline: t.Redlines++; break;
                        }
                        switch (row.Outcome)
                        {
                            case RoundOutcome.Correct:
                                t.Correct++;
                                t.DifficultyPoints += 1 + (int)view.Question!.Difficulty;
                                break;
                            case RoundOutcome.SpunOut: t.SpinOuts++; break;
                            case RoundOutcome.NoAnswer: t.NoAnswers++; break;
                        }
                    }
                    var p1 = view.Drivers.Where(r => r.Position == 1).ToList();
                    leaders.Add(p1[0].Code);
                    aiLed.Add(p1.All(r => r.Player is null));
                    break;
                case RacePhase.Chequered:
                    finalRound = view.Round;
                    final = [.. view.Drivers.Select(r =>
                    {
                        var t = tally[r.Code];
                        // A wrong Redline answer with no answer given counts as a spin-out, not a NoAnswer; both are rare.
                        return new CarRecord(r.Code, skillOf[r.Code], r.Player is { } p ? botOf[p] : -1,
                            r.Distance, r.Position, t.Correct, t.Cruises, t.Pushes, t.Redlines, t.SpinOuts, t.NoAnswers,
                            t.DifficultyPoints, gridSlot![r.Code],
                            qualiOf.GetValueOrDefault(r.Code).Correct, qualiOf.GetValueOrDefault(r.Code).TotalMs);
                    })];
                    break;
            }
        }

        void Perform(int bot, Act act, RedlineHostView view)
        {
            var spec = humans[bot];
            var phone = h.PlayerView(bot);
            switch (act)
            {
                case Act.Claim when phone is ChoiceView pick && pick.Options.Count > 0:
                {
                    var choice = o.Seats == SeatPick.Top
                        ? pick.Options.MaxBy(opt => pointsOf[opt.Id])!
                        : pick.Options[botRandom.Next(pick.Options.Count)];
                    h.Input(bot, new { selected = new[] { choice.Id } });
                    break;
                }
                case Act.Throttle when phone is ButtonsView buttons:
                {
                    var difficulty = BotBrain.ParseDifficulty(buttons.Prompt ?? "");
                    var p = spec.Skill.P(difficulty);
                    var me = view.Drivers.Single(r => r.Player == h.Players[bot].Id);
                    var leader = view.Drivers.Max(r => r.Distance);
                    var throttle = spec.Strategy switch
                    {
                        Strategy.AlwaysCruise => Throttle.Cruise,
                        Strategy.AlwaysPush => Throttle.Push,
                        Strategy.AlwaysRedline => Throttle.Redline,
                        Strategy.Random => (Throttle)botRandom.Next(3),
                        Strategy.Sensible => BotBrain.Sensible(p, limiterCost[bot], o.Rules),
                        Strategy.Chaser => BotBrain.Chaser(p, limiterCost[bot], me.Distance, leader, view.Round, o.Rules),
                        _ => throw new ArgumentOutOfRangeException(nameof(act)),
                    };
                    h.Input(bot, new { pressed = BotBrain.ButtonId(throttle) });
                    break;
                }
                case Act.Lap when phone is ChoiceView lap:
                {
                    // The phone measures from answers-on-screen to tap; in the sim that is exactly the time since the lap opened.
                    var (correct, _) = source.Lookup(lap.Prompt);
                    var right = lap.Options.Single(opt => opt.Label == correct);
                    var wrong = lap.Options.Where(opt => opt.Label != correct).ToList();
                    var id = qualiRandom.NextDouble() < o.Quali.Accuracy ? right.Id : wrong[qualiRandom.Next(wrong.Count)].Id;
                    h.Input(bot, new { selected = new[] { id }, elapsedMs = (h.Now - lapOpenedAt!.Value).TotalMilliseconds });
                    break;
                }
                case Act.Answer when phone is ChoiceView question:
                {
                    var (correct, difficulty) = source.Lookup(question.Prompt);
                    var right = question.Options.Single(opt => opt.Label == correct);
                    var wrong = question.Options.Where(opt => opt.Label != correct).ToList();
                    var pickRight = botRandom.NextDouble() < spec.Skill.P(difficulty);
                    var id = pickRight ? right.Id : wrong[botRandom.Next(wrong.Count)].Id;
                    h.Input(bot, new { selected = new[] { id } });
                    break;
                }
            }
        }
    }
}

/// <summary>Stable seeds: string.GetHashCode and HashCode are randomised per process, so neither is used.</summary>
public static class Seeds
{
    public static int For(int seed, string scenario, int race)
    {
        ulong x = 1469598103934665603UL;
        foreach (var ch in scenario) x = (x ^ ch) * 1099511628211UL;
        x ^= (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
        x ^= (ulong)(uint)race + 0xBF58476D1CE4E5B9UL;
        // splitmix64 finaliser
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (int)(x & 0x7FFFFFFF);
    }
}
