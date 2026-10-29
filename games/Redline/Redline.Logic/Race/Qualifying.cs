namespace Redline.Logic;

/// <summary>
/// Qualifying spec §2.1–2.3: the pure maths of the three timed qualifying laps. The lap-time clamp, the AI's laps, the
/// classification and the fastest lap. The game phases that use them live in <see cref="RedlineGame"/>.
/// </summary>
public static class Qualifying
{
    /// <summary>The shortest lap time a phone may claim, in milliseconds (§2.1).</summary>
    public const int MinLapMs = 200;

    /// <summary>The most a phone's own clock may undercut the server's measurement, in milliseconds (§2.1).</summary>
    public const int MaxGainMs = 1000;

    /// <summary>The quickest AI lap, in milliseconds (§2.3).</summary>
    public const int AiFloorMs = 600;

    /// <summary>The aiSkill whose median is <see cref="AiRules.QualiSlowMedian"/>: the bottom of the grid's range.</summary>
    private const double SlowSkill = 0.85;

    /// <summary>The aiSkill whose median is <see cref="AiRules.QualiFastMedian"/>: the top of the grid's range.</summary>
    private const double FastSkill = 1.15;

    /// <summary>
    /// §2.1: the lap time in whole milliseconds, <c>clamp(t, max(200, s − 1000), s)</c> for the phone's measurement
    /// <paramref name="reportedMs"/> (t) and the server's <paramref name="serverMs"/> (s). A missing or non-finite t is s.
    /// The upper bound stops a phone claiming more time than passed; the lower bound limits a modified client or a page
    /// reload to 1 s of gain. When s is below the 200 ms floor the bounds cross and s wins.
    /// </summary>
    public static int LapTimeMs(double? reportedMs, int serverMs)
    {
        if (reportedMs is not { } t || !double.IsFinite(t)) return serverMs;
        var lower = Math.Min(Math.Max(MinLapMs, serverMs - MaxGainMs), serverMs);
        return (int)Math.Round(Math.Clamp(t, lower, serverMs));
    }

    /// <summary>
    /// §2.3: one AI qualifying lap. Correct with <c>AiPolicy.Accuracy(aiSkill, Easy, rules.Ai)</c>, decided exactly as
    /// <see cref="AiPolicy.Answer"/> does, so <paramref name="correctIndex"/> is the question's correct option (0..3).
    /// The time is log-normal, <c>median × exp(σ·z)</c>, clamped to [600 ms, <c>rules.QualifyingAnswer</c>]. It is null
    /// when it reaches the end of the window: no answer. A wrong AI still gets a time, as a human's wrong answer does.
    /// Always consumes exactly four <c>NextDouble()</c> draws, correctness first (two) and then the time (two, Box–Muller),
    /// so results are deterministic for a seed.
    /// </summary>
    public static (bool Correct, int? Ms) AiLap(double aiSkill, int correctIndex, RedlineRules rules, Random random)
    {
        var ai = rules.Ai;
        var p = AiPolicy.Accuracy(aiSkill, Difficulty.Easy, ai);
        var correct = AiPolicy.Answer(p, correctIndex, random) == correctIndex;

        var u1 = 1 - random.NextDouble();                                       // (0, 1]: log never sees 0
        var u2 = random.NextDouble();
        var z = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);      // standard normal

        var windowMs = rules.QualifyingAnswer.TotalMilliseconds;
        var ms = Math.Clamp(MedianSeconds(aiSkill, ai) * 1000 * Math.Exp(ai.QualiSigma * z), AiFloorMs, windowMs);
        return (correct, ms >= windowMs ? null : (int)Math.Round(ms));
    }

    /// <summary>§2.3: the AI's median lap in seconds, linear in skill between the slow and fast medians, clamped at the ends.</summary>
    private static double MedianSeconds(double aiSkill, AiRules ai)
    {
        var t = Math.Clamp((aiSkill - SlowSkill) / (FastSkill - SlowSkill), 0, 1);
        return ai.QualiSlowMedian + (ai.QualiFastMedian - ai.QualiSlowMedian) * t;
    }

    /// <summary>
    /// §2.2: the qualifying order, <c>QualiCorrect</c> desc, then <c>QualiTotalMs</c> asc, then <c>QualiTieOrder</c> asc.
    /// The seeded order settles exact ties, so the result is deterministic, and it is the whole order for a field with
    /// no correct laps.
    /// </summary>
    public static IReadOnlyList<Driver> Classify(IEnumerable<Driver> field) =>
        [.. field.OrderByDescending(d => d.QualiCorrect).ThenBy(d => d.QualiTotalMs).ThenBy(d => d.QualiTieOrder)];

    /// <summary>
    /// §2.2: the code of the car with the lowest correct time in the latest lap, from the latest entry of each driver's
    /// <c>QualiLaps</c>; a deleted lap (null) or no lap yet does not count. Ties go to the better classified, so pass the
    /// <see cref="Classify"/> order. Null when nobody has a time.
    /// </summary>
    public static string? FastestLap(IEnumerable<Driver> classified)
    {
        string? code = null;
        var best = int.MaxValue;
        foreach (var d in classified)
        {
            if (d.QualiLaps is not [.., { } ms] || ms >= best) continue;       // strict <: the first, best classified, keeps a tie
            best = ms;
            code = d.Code;
        }
        return code;
    }
}
