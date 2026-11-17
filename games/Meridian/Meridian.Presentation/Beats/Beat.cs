namespace Meridian.Presentation.Beats;

/// <summary>
/// One step of a beat's timeline. A step that <see cref="WaitsForVoice"/> holds the timeline at its end until Tiller has
/// finished speaking (or the estimate has run out), because a voice's length isn't known until it plays.
/// </summary>
public sealed record BeatStep(string Name, double Seconds, bool WaitsForVoice = false);

/// <summary>
/// How the TV shows one presented event (M4 plan, Task 4): a named timeline the scene animates, step by step. The kinds
/// and their steps are the plan's table; a beat that would outrun its fallback is scaled down to fit.
/// </summary>
/// <param name="Speaks">Tiller reads this event aloud; the timeline waits for the voice.</param>
/// <param name="SpeakSeconds">How long the voice should take at the rules' rate: the wait's estimate.</param>
/// <param name="Short">The brief form, for a TV that has just connected: show it, don't replay it.</param>
public sealed record Beat(int Seq, string Kind, Presented Now, EqArray<BeatStep> Steps, bool Speaks, double SpeakSeconds, bool Short)
{
    /// <summary>A silent beat finishes within this share of its fallback, so the TV signals before the server gives up.</summary>
    public const double SilentShare = 0.85;

    /// <summary>How long a voice may overrun its estimate before the beat moves on without it.</summary>
    public const double VoiceGrace = 2.0;

    /// <summary>The kinds Tiller reads aloud (M4 plan, Task 5 decides the words).</summary>
    public static readonly IReadOnlySet<string> SpokenKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "card", "breach", "scenario", "notice", "vote", "over", "death", "round",
    };

    /// <summary>The timeline, without waiting for the voice.</summary>
    public double Seconds => Steps.Sum(s => s.Seconds);

    /// <summary>When the step that waits for the voice ends, on the timeline; null if none does.</summary>
    public double? VoiceAt
    {
        get
        {
            var at = 0.0;
            foreach (var step in Steps)
            {
                at += step.Seconds;
                if (step.WaitsForVoice)
                {
                    return at;
                }
            }
            return null;
        }
    }

    public static Beat For(Presented now, MeridianRules rules, bool brief = false)
    {
        if (brief)
        {
            return new Beat(now.Seq, now.Kind, now, [new BeatStep("show", 0.5)], false, 0, true);
        }
        var speaks = SpokenKinds.Contains(now.Kind) && !string.IsNullOrWhiteSpace($"{now.Title}{now.Text}");
        if (speaks)
        {
            // Panel in, then the voice, then a moment to take it in, then out: a second in all (the rules' reading lead).
            EqArray<BeatStep> spoken = [new BeatStep("in", 0.3, WaitsForVoice: true), new BeatStep("hold", 0.5), new BeatStep("out", 0.2)];
            return new Beat(now.Seq, now.Kind, now, spoken, true, SpeakingSeconds(now, rules), false);
        }
        EqArray<BeatStep> steps = now.Kind switch
        {
            "reveal" => [new("ease", 0.3), new("drop", 0.4), new("hatches", 0.2), new("walk", 0.2)],
            "roll" => [new("tumble", 0.8), new("land", 0.3), new("total", 0.3)],
            "fight" => [new("flash", 0.4), new("dice", 0.6), new("tick", 0.4)],
            "sealed" => [new("weld", 0.4), new("hold", 0.2)],
            _ => [new("show", 0.6)],
        };
        return new Beat(now.Seq, now.Kind, now, Fit(steps, now.Seconds * SilentShare), false, 0, false);
    }

    /// <summary>The voice's estimate: title and text at the rules' speaking rate.</summary>
    public static double SpeakingSeconds(Presented now, MeridianRules rules)
        => $"{now.Title} {now.Text}".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length / rules.ReadingWordsPerSecond;

    private static EqArray<BeatStep> Fit(EqArray<BeatStep> steps, double limit)
    {
        var total = steps.Sum(s => s.Seconds);
        return total <= limit ? steps : steps.Select(s => s with { Seconds = s.Seconds * limit / total }).ToEqArray();
    }
}
