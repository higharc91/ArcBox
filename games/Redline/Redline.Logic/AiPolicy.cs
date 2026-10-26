namespace Redline.Logic;

/// <summary>Pure, deterministic AI decisions; all randomness comes from the supplied <see cref="Random"/>.</summary>
public static class AiPolicy
{
    /// <summary>P(correct) = clamp(base accuracy of the difficulty x aiSkill, MinP, MaxP).</summary>
    public static double Accuracy(double aiSkill, Difficulty difficulty, AiRules rules)
    {
        var baseP = difficulty switch
        {
            Difficulty.Easy => rules.Easy,
            Difficulty.Medium => rules.Medium,
            Difficulty.Hard => rules.Hard,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
        };
        return Math.Clamp(baseP * aiSkill, rules.MinP, rules.MaxP);
    }

    /// <summary>
    /// Expected distance gained this round for each throttle at P(correct) = <paramref name="p"/>, read from the
    /// payout table in <paramref name="rules"/>:
    /// Cruise = p·CruiseGain; Push = p·PushGain − (1−p)·PushLoss;
    /// Redline = p·RedlineGain − (1−p)·(RedlineLoss + Ai.LimiterCost), plus Ai.ChaseBonus in chase mode.
    /// The limiter cost is the value of the round lost to the pit limiter, so it is charged only on a wrong answer.
    /// </summary>
    public static (double Cruise, double Push, double Redline) Scores(double p, int gapToLeader, int roundsLeft, RedlineRules rules)
    {
        var ai = rules.Ai;
        var q = 1 - p;
        return (
            p * rules.CruiseGain,
            p * rules.PushGain - q * rules.PushLoss,
            p * rules.RedlineGain - q * (rules.RedlineLoss + ai.LimiterCost) + (IsChasing(gapToLeader, roundsLeft, ai) ? ai.ChaseBonus : 0));
    }

    /// <summary>Chase mode: at least <c>ChaseGap</c> behind the leader with at most <c>ChaseRoundsLeft</c> rounds left.</summary>
    public static bool IsChasing(int gapToLeader, int roundsLeft, AiRules ai) =>
        gapToLeader >= ai.ChaseGap && roundsLeft <= ai.ChaseRoundsLeft;

    /// <summary>
    /// Softmax over the expected-value <see cref="Scores"/>. <paramref name="roundsLeft"/> counts the current round.
    /// Redline is off the table below <c>Ai.RedlineFloor</c> unless chasing.
    /// Always consumes exactly one <c>NextDouble()</c> draw.
    /// </summary>
    public static Throttle ChooseThrottle(double p, int gapToLeader, int roundsLeft, RedlineRules rules, Random random)
    {
        var ai = rules.Ai;
        var chasing = IsChasing(gapToLeader, roundsLeft, ai);
        var (cruise, push, redline) = Scores(p, gapToLeader, roundsLeft, rules);

        Span<double> score = [cruise, push, redline];
        var redlineAllowed = chasing || p >= ai.RedlineFloor;
        var count = redlineAllowed ? 3 : 2;

        var max = double.NegativeInfinity;
        for (var i = 0; i < count; i++) max = Math.Max(max, score[i]);

        Span<double> weight = stackalloc double[3];
        var total = 0.0;
        for (var i = 0; i < count; i++)
        {
            weight[i] = Math.Exp((score[i] - max) / ai.Temperature);
            total += weight[i];
        }

        var pick = random.NextDouble() * total;
        for (var i = 0; i < count; i++)
        {
            pick -= weight[i];
            if (pick < 0) return (Throttle)i;
        }
        return (Throttle)(count - 1);
    }

    /// <summary>
    /// Returns <paramref name="correctIndex"/> with probability p, otherwise a uniform pick among the
    /// other three of 0..3. Always consumes exactly two <c>NextDouble()</c> draws.
    /// </summary>
    public static int Answer(double p, int correctIndex, Random random)
    {
        var hit = random.NextDouble() < p;
        var wrongPick = Math.Min((int)(random.NextDouble() * 3), 2);
        if (hit) return correctIndex;
        return wrongPick < correctIndex ? wrongPick : wrongPick + 1;
    }
}
