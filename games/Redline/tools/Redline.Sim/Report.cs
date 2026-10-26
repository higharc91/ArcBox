using System.Globalization;
using System.Text;
using Redline.Logic;

namespace Redline.Sim;

/// <summary>Turns per-race records into Markdown tables.</summary>
public static class Report
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string F(double x, int digits = 1) => x.ToString("F" + digits, Inv);
    private static string Pct(double x) => (100 * x).ToString("F1", Inv) + "%";
    private static string Min(double seconds) => (seconds / 60).ToString("F1", Inv);

    private static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        var i = (int)Math.Clamp(Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[i];
    }

    /// <summary>
    /// Wall-clock bound with every phase running to its timer: Driver Select + Grid + rounds × (Throttle + Question + Reveal) +
    /// Chequered. The Reading phases depend on each question's text, so the caller adds <see cref="RaceRecord.ReadingSeconds"/>.
    /// </summary>
    public static double FullTimerSeconds(int rounds, RedlineRules r) =>
        (r.DriverSelect + r.Grid + rounds * (r.Throttle + r.Question + r.Reveal) + r.Chequered).TotalSeconds;

    public static string Header(SimOptions o, TimeSpan elapsed, int totalRaces)
    {
        var r = o.Rules;
        var a = r.Ai;
        var sb = new StringBuilder();
        sb.AppendLine("# REDLINE balance simulation");
        sb.AppendLine();
        sb.AppendLine($"- Races per scenario: **{o.Races}** ({totalRaces} total, {F(elapsed.TotalSeconds, 0)} s wall time), master seed {o.Seed}");
        sb.AppendLine($"- Rules: FinishLine {r.FinishLine}, MaxRounds {r.MaxRounds}, Cruise +{r.CruiseGain}/0, Push +{r.PushGain}/−{r.PushLoss}, Redline +{r.RedlineGain}/−{r.RedlineLoss} + limiter, Reading phase {(r.ReadQuestions ? "on" : "off")}");
        sb.AppendLine($"- AI: base {F(a.Easy, 2)}/{F(a.Medium, 2)}/{F(a.Hard, 2)}, clamp [{F(a.MinP, 2)}, {F(a.MaxP, 2)}], temperature {F(a.Temperature, 2)}, limiter cost {F(a.LimiterCost, 2)}, Redline floor {F(a.RedlineFloor, 2)}, chase gap {a.ChaseGap} / rounds left {a.ChaseRoundsLeft} / bonus {F(a.ChaseBonus, 2)}");
        sb.AppendLine($"- Question mix: {o.Mix}; human latency: {o.Latency.Name}; human seat pick: {o.Seats}");
        sb.AppendLine($"- Skills: casual {F(Skill.Casual.Easy, 2)}/{F(Skill.Casual.Medium, 2)}/{F(Skill.Casual.Hard, 2)}, strong {F(Skill.Strong.Easy, 2)}/{F(Skill.Strong.Medium, 2)}/{F(Skill.Strong.Hard, 2)}; limiter cost used by Sensible/Chaser: casual {F(BotBrain.LimiterCost(Skill.Casual, o.Mix, r), 2)}, strong {F(BotBrain.LimiterCost(Skill.Strong, o.Mix, r), 2)}");
        sb.AppendLine();
        return sb.ToString();
    }

    public static string Overview(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs, RedlineRules rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Race overview");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Humans | Rounds mean | p10/p50/p90 | Ended at line | Human win | Shared P1 | …with difficulty tie-break | AI led (of rounds) | Lead changes | P1−P2 gap | gap ≤1 | P1 dist | P11 dist |");
        sb.AppendLine("|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (s, r) in runs)
        {
            var rounds = r.Select(x => (double)x.Rounds).Order().ToList();
            var humanWin = r.Count(x => x.Cars.Any(c => c.Position == 1 && c.Bot >= 0)) / (double)r.Length;
            var shared = r.Count(x => x.Cars.Count(c => c.Position == 1) > 1) / (double)r.Length;
            // Shared P1 that a third key (difficulty-weighted correct answers) would still not split.
            var sharedWeighted = r.Count(x =>
            {
                var p1 = x.Cars.Where(c => c.Position == 1).ToList();
                return p1.Count > 1 && p1.Count(c => c.DifficultyPoints == p1.Max(m => m.DifficultyPoints)) > 1;
            }) / (double)r.Length;
            var aiLed = r.Sum(x => x.AiLedRound.Count(b => b)) / (double)r.Sum(x => x.AiLedRound.Length);
            var changes = r.Average(x => LeadChanges(x.LeaderByRound));
            var gaps = r.Select(x => x.Cars[0].Distance - x.Cars[1].Distance).ToList();
            sb.AppendLine($"| {s.Name} | {s.Humans.Count} | {F(rounds.Average())} | {Quantile(rounds, .1)}/{Quantile(rounds, .5)}/{Quantile(rounds, .9)} | " +
                          $"{Pct(r.Count(x => x.EndedByLine) / (double)r.Length)} | {Pct(humanWin)} | {Pct(shared)} | {Pct(sharedWeighted)} | {Pct(aiLed)} | {F(changes)} | " +
                          $"{F(gaps.Average(), 2)} | {Pct(gaps.Count(g => g <= 1) / (double)gaps.Count)} | {F(r.Average(x => x.Cars[0].Distance))} | {F(r.Average(x => x.Cars[10].Distance))} |");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static int LeadChanges(string[] leaders)
    {
        var n = 0;
        for (var i = 1; i < leaders.Length; i++) if (leaders[i] != leaders[i - 1]) n++;
        return n;
    }

    public static string RoundHistogram(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs, RedlineRules rules)
    {
        var sb = new StringBuilder();
        var from = Math.Max(1, rules.MaxRounds - 9);
        sb.AppendLine("## Race length (share of races ending after round n)");
        sb.AppendLine();
        sb.Append("| Scenario | <" + from + " |");
        for (var n = from; n <= rules.MaxRounds; n++) sb.Append($" {n} |");
        sb.AppendLine(" of which R" + rules.MaxRounds + " with nobody at the line |");
        sb.Append("|---|---:|");
        for (var n = from; n <= rules.MaxRounds; n++) sb.Append("---:|");
        sb.AppendLine("---:|");
        foreach (var (s, r) in runs)
        {
            sb.Append($"| {s.Name} | {Pct(r.Count(x => x.Rounds < from) / (double)r.Length)} |");
            for (var n = from; n <= rules.MaxRounds; n++) sb.Append($" {Pct(r.Count(x => x.Rounds == n) / (double)r.Length)} |");
            sb.AppendLine($" {Pct(r.Count(x => !x.EndedByLine) / (double)r.Length)} |");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public static string Humans(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Human bots (per seat; positions out of 22)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Bot | Seats | Win | Top 3 | Top 5 | Mean pos | Median pos | Mean dist | Correct | Cruise/Push/Redline | Spin-outs/race | No answer/race |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|");
        foreach (var (s, r) in runs)
        {
            foreach (var group in s.Humans.Select((b, i) => (b, i)).GroupBy(x => x.b.Label))
            {
                var idx = group.Select(x => x.i).ToHashSet();
                var cars = r.SelectMany(x => x.Cars.Where(c => idx.Contains(c.Bot)).Select(c => (Car: c, Race: x))).ToList();
                if (cars.Count == 0) continue;
                var positions = cars.Select(c => (double)c.Car.Position).Order().ToList();
                var answered = cars.Sum(c => c.Race.Rounds);
                var throttles = cars.Sum(c => c.Car.Cruises + c.Car.Pushes + c.Car.Redlines);
                sb.AppendLine($"| {s.Name} | {group.Key} | {idx.Count} | {Pct(cars.Count(c => c.Car.Position == 1) / (double)cars.Count)} | " +
                              $"{Pct(cars.Count(c => c.Car.Position <= 3) / (double)cars.Count)} | {Pct(cars.Count(c => c.Car.Position <= 5) / (double)cars.Count)} | " +
                              $"{F(positions.Average())} | {Quantile(positions, .5)} | {F(cars.Average(c => c.Car.Distance))} | " +
                              $"{Pct(cars.Sum(c => c.Car.Correct) / (double)answered)} | " +
                              $"{Pct(cars.Sum(c => c.Car.Cruises) / (double)throttles)}/{Pct(cars.Sum(c => c.Car.Pushes) / (double)throttles)}/{Pct(cars.Sum(c => c.Car.Redlines) / (double)throttles)} | " +
                              $"{F(cars.Average(c => c.Car.SpinOuts), 2)} | {F(cars.Average(c => c.Car.NoAnswers), 2)} |");
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>AI cars grouped by aiSkill tier: top (≥ 1.08: ANT RUS HAM VER LEC NOR), mid, back (≤ 0.90).</summary>
    public static string Ai(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs)
    {
        static string Tier(double skill) => skill >= 1.08 ? "top" : skill > 0.90 ? "mid" : "back";
        var sb = new StringBuilder();
        sb.AppendLine("## AI cars by aiSkill tier (top ≥ 1.08, back ≤ 0.90)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | AI wins | won by top tier | by mid | by back | Top-tier mean pos | Mid mean pos | Back mean pos | AI Redline share | AI spin-outs/car/race | AI in chase mode | Best AI dist |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (s, r) in runs)
        {
            var ai = r.SelectMany(x => x.Cars.Where(c => c.Bot < 0)).ToList();
            var aiWins = r.Where(x => x.Cars[0].Bot < 0 && !x.Cars.Any(c => c.Position == 1 && c.Bot >= 0)).ToList();
            double WonBy(string tier) => aiWins.Count == 0 ? 0 : aiWins.Count(x => Tier(x.Cars[0].AiSkill) == tier) / (double)aiWins.Count;
            string MeanPos(string tier)
            {
                var t = ai.Where(c => Tier(c.AiSkill) == tier).ToList();
                return t.Count == 0 ? "–" : F(t.Average(c => c.Position));
            }
            var throttles = ai.Sum(c => c.Cruises + c.Pushes + c.Redlines);
            sb.AppendLine($"| {s.Name} | {Pct(aiWins.Count / (double)r.Length)} | {Pct(WonBy("top"))} | {Pct(WonBy("mid"))} | {Pct(WonBy("back"))} | " +
                          $"{MeanPos("top")} | {MeanPos("mid")} | {MeanPos("back")} | {Pct(ai.Sum(c => c.Redlines) / (double)Math.Max(1, throttles))} | " +
                          $"{F(ai.Average(c => c.SpinOuts), 2)} | {Pct(r.Sum(x => x.AiChaseDecisions) / (double)Math.Max(1, r.Sum(x => x.AiThrottleDecisions)))} | {F(r.Average(x => x.Cars.Where(c => c.Bot < 0).Max(c => c.Distance)))} |");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public static string Timing(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs, RedlineRules rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Wall-clock game time (minutes)");
        sb.AppendLine();
        sb.AppendLine("Measured = the harness clock from Start to EndGame with the bots' reaction times (early advance as the code does it). " +
                      "All timers = every phase runs to its timer for the same number of rounds. Reading = mean seconds per round spent " +
                      "with the question read out before answers open (it never ends early).");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Measured mean | p10 | p90 | All timers mean | p90 | Saved by early advance | Reading s/round | Throttle timeouts | Answer timeouts |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (s, r) in runs)
        {
            var measured = r.Select(x => x.GameSeconds).Order().ToList();
            var full = r.Select(x => FullTimerSeconds(x.Rounds, rules) + x.ReadingSeconds).Order().ToList();
            var reading = r.Sum(x => x.ReadingSeconds) / r.Sum(x => x.Rounds);
            var td = r.Sum(x => x.ThrottleDecisions);
            var ad = r.Sum(x => x.AnswerDecisions);
            sb.AppendLine($"| {s.Name} | {Min(measured.Average())} | {Min(Quantile(measured, .1))} | {Min(Quantile(measured, .9))} | " +
                          $"{Min(full.Average())} | {Min(Quantile(full, .9))} | {Pct(1 - measured.Average() / full.Average())} | {F(reading)} | " +
                          $"{(td == 0 ? "–" : Pct(r.Sum(x => x.ThrottleTimeouts) / (double)td))} | {(ad == 0 ? "–" : Pct(r.Sum(x => x.AnswerTimeouts) / (double)ad))} |");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>For scenarios with more than one bot label: P(row bot finishes ahead of column bot), ties counting half.</summary>
    public static string HeadToHead(IReadOnlyList<(Scenario S, RaceRecord[] R)> runs)
    {
        var sb = new StringBuilder();
        foreach (var (s, r) in runs)
        {
            var labels = s.Humans.Select(b => b.Label).Distinct().ToList();
            if (labels.Count < 2) continue;
            sb.AppendLine($"## Head to head: {s.Name}");
            sb.AppendLine();
            sb.AppendLine("P(row finishes ahead of column), shared positions count half. Win share is per seat, shared wins included.");
            sb.AppendLine();
            sb.Append("| ↓ ahead of → |");
            foreach (var l in labels) sb.Append($" {l} |");
            sb.AppendLine(" Win share |");
            sb.Append("|---|");
            foreach (var _ in labels) sb.Append("---:|");
            sb.AppendLine("---:|");
            foreach (var a in labels)
            {
                sb.Append($"| {a} |");
                var ia = Indices(s, a);
                foreach (var b in labels)
                {
                    if (a == b) { sb.Append(" – |"); continue; }
                    var ib = Indices(s, b);
                    double score = 0; var n = 0;
                    foreach (var race in r)
                        foreach (var ca in race.Cars.Where(c => ia.Contains(c.Bot)))
                            foreach (var cb in race.Cars.Where(c => ib.Contains(c.Bot)))
                            {
                                n++;
                                score += ca.Position < cb.Position ? 1 : ca.Position == cb.Position ? 0.5 : 0;
                            }
                    sb.Append($" {Pct(score / n)} |");
                }
                var wins = r.Sum(x => x.Cars.Count(c => ia.Contains(c.Bot) && c.Position == 1));
                sb.AppendLine($" {Pct(wins / (double)(r.Length * ia.Count))} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static HashSet<int> Indices(Scenario s, string label) =>
        s.Humans.Select((b, i) => (b, i)).Where(x => x.b.Label == label).Select(x => x.i).ToHashSet();
}
