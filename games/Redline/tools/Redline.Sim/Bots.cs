using Redline.Logic;

namespace Redline.Sim;

public enum Strategy { AlwaysCruise, AlwaysPush, AlwaysRedline, Random, Sensible, Chaser }

/// <summary>A human player's chance of answering correctly at each difficulty.</summary>
public sealed record Skill(string Name, double Easy, double Medium, double Hard)
{
    public static readonly Skill Casual = new("casual", 0.80, 0.55, 0.35);
    public static readonly Skill Strong = new("strong", 0.95, 0.80, 0.60);

    public double P(Difficulty d) => d switch
    {
        Difficulty.Easy => Easy,
        Difficulty.Medium => Medium,
        Difficulty.Hard => Hard,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };
}

/// <summary>One simulated human seat: how it bets and how well it knows its trivia.</summary>
public sealed record BotSpec(Strategy Strategy, Skill Skill)
{
    public string Label => $"{Strategy}/{Skill.Name}";
}

/// <summary>Human reaction times. Each decision is drawn from a log-normal; one past the phase deadline is a timeout.</summary>
public sealed record Latency(string Name, double ClaimMedian, double ThrottleMedian, double AnswerMedian, double Sigma)
{
    /// <summary>Medians: claim 6 s, throttle 3 s, answer 7 s; sigma 0.45. About 0.4% of throttles and 1% of answers time out.</summary>
    public static readonly Latency Model = new("model", 6, 3, 7, 0.45);

    /// <summary>Every bot acts the instant its view appears: the fastest the code can possibly run.</summary>
    public static readonly Latency Instant = new("instant", 0, 0, 0, 0);

    public TimeSpan Draw(double median, Random random)
    {
        if (median <= 0) return TimeSpan.Zero;
        // Box–Muller standard normal.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return TimeSpan.FromSeconds(median * Math.Exp(Sigma * z));
    }
}

/// <summary>
/// Human qualifying laps (qualifying spec §7): every bot answers each lap at a log-normal time, median 2.6 s, σ 0.3 (of
/// ln time), and is right with probability 0.8 whatever its race skill (qualifying questions are all easy, and 0.8 is
/// the casual player's easy accuracy). The phone's <c>elapsedMs</c> is the drawn time.
/// </summary>
public sealed record QualiModel(double Median, double Sigma, double Accuracy)
{
    public static readonly QualiModel Default = new(2.6, 0.3, 0.8);

    public TimeSpan Draw(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return TimeSpan.FromSeconds(Median * Math.Exp(Sigma * z));
    }
}

/// <summary>Throttle decisions for the human bots. Everything they use is on the phone or on the TV.</summary>
public static class BotBrain
{
    /// <summary>
    /// Expected distance gained this round for each throttle, where a spin-out also costs
    /// <paramref name="limiterCost"/> (the value lost by being forced to Cruise next round).
    /// </summary>
    public static (double Cruise, double Push, double Redline) Ev(double p, double limiterCost, RedlineRules r) =>
        (p * r.CruiseGain,
         p * r.PushGain - (1 - p) * r.PushLoss,
         p * r.RedlineGain - (1 - p) * (r.RedlineLoss + limiterCost));

    /// <summary>
    /// The pit limiter's cost for this player: E[best EV next round] − E[Cruise EV next round], solved as a fixed point
    /// because the best EV itself depends on the limiter cost.
    /// </summary>
    public static double LimiterCost(Skill s, DifficultyMix mix, RedlineRules r)
    {
        var l = 0.0;
        for (var i = 0; i < 100; i++)
        {
            var best = 0.0;
            var cruise = 0.0;
            foreach (var d in Enum.GetValues<Difficulty>())
            {
                var (c, pu, re) = Ev(s.P(d), l, r);
                best += mix.Weight(d) * Math.Max(c, Math.Max(pu, re));
                cruise += mix.Weight(d) * c;
            }
            l = best - cruise;
        }
        return l;
    }

    public static Throttle Sensible(double p, double limiterCost, RedlineRules r)
    {
        var (c, pu, re) = Ev(p, limiterCost, r);
        if (re >= pu && re >= c) return Throttle.Redline;
        return pu >= c ? Throttle.Push : Throttle.Cruise;
    }

    /// <summary>
    /// Chaser: Sensible, but REDLINE when it is "late" (the leader is within 6 of the flag or ≤ 5 rounds remain,
    /// counting this one) and it is at least 3 units behind the leader.
    /// </summary>
    public static Throttle Chaser(double p, double limiterCost, int myDistance, int leaderDistance, int round, RedlineRules r)
    {
        var roundsLeft = r.MaxRounds - round + 1;
        var late = leaderDistance >= r.FinishLine - 6 || roundsLeft <= 5;
        return late && leaderDistance - myDistance >= 3 ? Throttle.Redline : Sensible(p, limiterCost, r);
    }

    public static string ButtonId(Throttle t) => t switch
    {
        Throttle.Cruise => "cruise",
        Throttle.Push => "push",
        Throttle.Redline => "redline",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    /// <summary>Reads the difficulty off the throttle prompt, "{Category} · {Difficulty}. How hard do you push?".</summary>
    public static Difficulty ParseDifficulty(string prompt)
    {
        const string tail = ". How hard do you push?";
        var end = prompt.LastIndexOf(tail, StringComparison.Ordinal);
        var dot = prompt.LastIndexOf(" · ", end < 0 ? prompt.Length - 1 : end, StringComparison.Ordinal);
        if (end < 0 || dot < 0) throw new FormatException($"Unrecognised throttle prompt '{prompt}'.");
        return Enum.Parse<Difficulty>(prompt[(dot + 3)..end]);
    }
}
