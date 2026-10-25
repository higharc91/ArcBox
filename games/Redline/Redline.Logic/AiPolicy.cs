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
    /// Softmax over expected-value scores. <paramref name="roundsLeft"/> counts the current round.
    /// Always consumes exactly one <c>NextDouble()</c> draw.
    /// </summary>
    public static Throttle ChooseThrottle(double p, int gapToLeader, int roundsLeft, AiRules rules, Random random)
    {
        var chasing = gapToLeader >= rules.ChaseGap && roundsLeft <= rules.ChaseRoundsLeft;

        Span<double> score =
        [
            p,
            3 * p - 1,
            6 * p - 2 - rules.LimiterCost + (chasing ? rules.ChaseBonus : 0),
        ];
        var redlineAllowed = chasing || p >= rules.RedlineFloor;
        var count = redlineAllowed ? 3 : 2;

        var max = double.NegativeInfinity;
        for (var i = 0; i < count; i++) max = Math.Max(max, score[i]);

        Span<double> weight = stackalloc double[3];
        var total = 0.0;
        for (var i = 0; i < count; i++)
        {
            weight[i] = Math.Exp((score[i] - max) / rules.Temperature);
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
