namespace Redline.Logic;

/// <summary>The §2.2 payout table: one car's distance after one round.</summary>
public static class Scoring
{
    /// <param name="correct">True or false for an answer; null when the driver gave none (scored as wrong).</param>
    /// <returns>
    /// The new distance (never below 0), the round outcome, and whether the driver spun out
    /// (a wrong or missing answer on Redline, which earns the pit limiter for the next round).
    /// </returns>
    public static (int NewDistance, RoundOutcome Outcome, bool SpinOut) Apply(int distance, Throttle throttle, bool? correct, RedlineRules rules)
    {
        if (correct == true)
        {
            var gain = throttle switch
            {
                Throttle.Cruise => rules.CruiseGain,
                Throttle.Push => rules.PushGain,
                Throttle.Redline => rules.RedlineGain,
                _ => throw new ArgumentOutOfRangeException(nameof(throttle)),
            };
            return (distance + gain, RoundOutcome.Correct, false);
        }

        var loss = throttle switch
        {
            Throttle.Cruise => 0,
            Throttle.Push => rules.PushLoss,
            Throttle.Redline => rules.RedlineLoss,
            _ => throw new ArgumentOutOfRangeException(nameof(throttle)),
        };
        var spinOut = throttle == Throttle.Redline;
        var outcome = spinOut ? RoundOutcome.SpunOut : correct is null ? RoundOutcome.NoAnswer : RoundOutcome.Wrong;
        return (Math.Max(0, distance - loss), outcome, spinOut);
    }
}
