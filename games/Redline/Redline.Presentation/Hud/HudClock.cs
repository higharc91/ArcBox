using Redline.Logic;

namespace Redline.Presentation.Hud;

/// <summary>
/// The phase clock. The server drives phases, so the clock is cosmetic: the time left is read once from
/// <see cref="RedlineHostView.DeadlineUtc"/> against the local clock when a view arrives, clamped to the phase length (a
/// skewed TV clock can't show 0:47 for a 20 s phase or a negative time), then counted down on the TV's own frame clock.
/// </summary>
public static class HudClock
{
    private static readonly RedlineRules Defaults = new();

    /// <summary>The longest Reading phase (<see cref="RedlineRules.ReadingTime"/> clamps to it).</summary>
    public const double MaxReadingSeconds = 10;

    /// <summary>
    /// The phase's nominal length (the shipped <see cref="RedlineRules"/>; the view doesn't carry timings). Reading depends
    /// on the question, so without the view it is the longest reading time.
    /// </summary>
    public static double PhaseSeconds(RacePhase phase, RedlineRules? rules = null)
    {
        var r = rules ?? Defaults;
        return phase switch
        {
            RacePhase.DriverSelect => r.DriverSelect.TotalSeconds,
            RacePhase.Grid => r.Grid.TotalSeconds,
            RacePhase.Throttle => r.Throttle.TotalSeconds,
            RacePhase.Reading => MaxReadingSeconds,
            RacePhase.Question => r.Question.TotalSeconds,
            RacePhase.Reveal => r.Reveal.TotalSeconds,
            RacePhase.Anthem => r.Anthem.TotalSeconds,
            RacePhase.Champagne => r.Champagne.TotalSeconds,
            _ => r.Chequered.TotalSeconds,
        };
    }

    /// <summary>The length of <paramref name="view"/>'s phase; Reading is <see cref="RedlineRules.ReadingTime"/> of its question.</summary>
    public static double PhaseSeconds(RedlineHostView view, RedlineRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view is { Phase: RacePhase.Reading, Question.Text: { Length: > 0 } text }
            ? (rules ?? Defaults).ReadingTime(text).TotalSeconds
            : PhaseSeconds(view.Phase, rules);
    }

    /// <summary>Seconds left at <paramref name="now"/>, in [0, <paramref name="phaseSeconds"/>]; null without a deadline.</summary>
    public static double? Remaining(DateTimeOffset? deadline, DateTimeOffset now, double phaseSeconds) =>
        deadline is { } d ? Math.Clamp((d - now).TotalSeconds, 0, Math.Max(0, phaseSeconds)) : null;

    /// <summary>"0:14": whole seconds rounded up, so the clock shows 0:00 only when time is really up.</summary>
    public static string Format(double seconds)
    {
        var s = (int)Math.Ceiling(Math.Max(0, seconds) - 1e-6);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Bar fill: the fraction of the phase still to run.</summary>
    public static double Fraction(double remaining, double phaseSeconds) =>
        phaseSeconds <= 0 ? 0 : Math.Clamp(remaining / phaseSeconds, 0, 1);
}
