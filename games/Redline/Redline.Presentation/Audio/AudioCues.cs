using Redline.Logic;

namespace Redline.Presentation.Audio;

/// <summary>
/// Audio spec §3.3: which sound effects a host-view transition plays, kept out of Godot so it can be tested. The names
/// are the TV's sound-bank keys (one per <see cref="Synth.Sfx"/> clip). Start-light beeps follow
/// <c>StartLights.Lit</c> on the scene clock, and spin-outs follow the camera, so the scene raises those itself.
/// </summary>
public static class AudioCues
{
    public const string StartBeep = "start-beep";
    public const string LightsOut = "lights-out";
    /// <summary>A player locked a throttle.</summary>
    public const string Lock = "lock";
    /// <summary>A player answered.</summary>
    public const string Answer = "tick";
    public const string Countdown = "countdown";
    public const string Whoosh = "whoosh";
    public const string Correct = "correct";
    public const string Wrong = "wrong";
    /// <summary>A player set the session's fastest lap (qualifying spec §6): a bright two-note chime.</summary>
    public const string Purple = "purple";
    public const string Screech = "screech";
    public const string Crunch = "crunch";
    public const string Cheer = "cheer";
    public const string Fanfare = "fanfare";
    /// <summary>The crowd murmur under the race (a loop).</summary>
    public const string CrowdBed = "crowd-bed";
    /// <summary>A champagne cork popping (podium spec §5), once per spray.</summary>
    public const string CorkPop = "cork-pop";
    /// <summary>The foam roaring out of the bottle, once per spray, its level and pitch following the strength.</summary>
    public const string SprayHiss = "spray-hiss";
    /// <summary>The victory theme's brass hit, with the first spray of the ceremony.</summary>
    public const string Sting = "victory-sting";

    /// <summary>Countdown ticks sound at this many seconds left and every second after, down to 1.</summary>
    public const int CountdownFrom = 5;

    /// <summary>
    /// The sounds of one champagne spray, in play order: the pop and the hiss, led by the victory sting on the
    /// ceremony's first spray.
    /// </summary>
    public static IReadOnlyList<string> ForSpray(bool first) => first ? [Sting, CorkPop, SprayHiss] : [CorkPop, SprayHiss];

    /// <summary>The one-shot cues for <paramref name="view"/> arriving after <paramref name="previous"/>, in play order.</summary>
    public static IReadOnlyList<string> ForView(RedlineHostView view, RedlineHostView? previous)
    {
        ArgumentNullException.ThrowIfNull(view);
        var cues = new List<string>(2);
        if (!IsNewPhase(view, previous))
        {
            switch (view.Phase)
            {
                case RacePhase.Throttle when Humans(view, DriverStatus.Locked) > Humans(previous!, DriverStatus.Locked):
                    cues.Add(Lock);
                    break;
                case RacePhase.Question or RacePhase.QualifyingQuestion
                    when Humans(view, DriverStatus.Answered) > Humans(previous!, DriverStatus.Answered):
                    cues.Add(Answer);
                    break;
            }
            return cues;
        }

        if (view.Phase == RacePhase.Throttle && previous?.Phase == RacePhase.Grid)
        {
            cues.Add(LightsOut);
        }
        if (ReadsQuestion(view, previous))
        {
            cues.Add(Whoosh);
        }
        switch (view.Phase)
        {
            case RacePhase.Reveal:
                cues.Add(PlayersRight(view) ? Correct : Wrong);
                if (NewLeader(view))
                {
                    cues.Add(Cheer);
                }
                break;
            case RacePhase.QualifyingResult:
                // Qualifying spec §6: the sting when any player's lap counted, else the buzz; then the chime for a player's purple lap.
                cues.Add(PlayersLapCounted(view) ? Correct : Wrong);
                if (PlayerPurple(view))
                {
                    cues.Add(Purple);
                }
                break;
            case RacePhase.Chequered:
                cues.Add(Fanfare);
                cues.Add(Cheer);
                break;
            case RacePhase.Champagne:
                // The anthem is over: the crowd erupts as the drivers reach for the bottles.
                cues.Add(Cheer);
                break;
        }
        return cues;
    }

    /// <summary>
    /// True when the question should be read out now: on entering Reading, or on entering Question straight from the
    /// same round's Throttle (Reading switched off). Qualifying reads the same way: on entering QualifyingReading, or on
    /// entering QualifyingQuestion from anything but its own Reading (the previous lap's Result, or Driver Select for lap 1).
    /// Never for a repeated view or a TV that joins mid-question.
    /// </summary>
    public static bool ReadsQuestion(RedlineHostView view, RedlineHostView? previous)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!IsNewPhase(view, previous) || view.Question?.Text is not { Length: > 0 })
        {
            return false;
        }
        return view.Phase is RacePhase.Reading or RacePhase.QualifyingReading
               || (view.Phase == RacePhase.Question && previous is { Phase: RacePhase.Throttle } p && p.Round == view.Round)
               || (view.Phase == RacePhase.QualifyingQuestion && previous is { Phase: not RacePhase.QualifyingReading });
    }

    /// <summary>Countdown ticks due as the clock runs from <paramref name="before"/> to <paramref name="after"/> seconds left.</summary>
    public static int CountdownTicks(RacePhase phase, double before, double after)
    {
        if (phase is not (RacePhase.Throttle or RacePhase.Question or RacePhase.QualifyingQuestion) || after >= before)
        {
            return 0;
        }
        var ticks = 0;
        for (var k = CountdownFrom; k >= 1; k--)
        {
            if (before > k && after <= k)
            {
                ticks++;
            }
        }
        return ticks;
    }

    private static bool IsNewPhase(RedlineHostView view, RedlineHostView? previous) =>
        previous is null || previous.Phase != view.Phase || previous.Round != view.Round;

    private static int Humans(RedlineHostView view, DriverStatus status) =>
        view.Drivers.Count(d => d.Player is not null && d.Status == status);

    /// <summary>One sound for the room: the sting when any player got it right (any car, with no players), else the buzz.</summary>
    private static bool PlayersRight(RedlineHostView view)
    {
        var players = view.Drivers.Where(d => d.Player is not null).ToList();
        return (players.Count > 0 ? players : view.Drivers).Any(d => d.Outcome == RoundOutcome.Correct);
    }

    /// <summary>
    /// One sound for the room at a QualifyingResult, as <see cref="PlayersRight"/>: true when any player's lap counted (it has a
    /// time; a wrong or missing answer is deleted), any car's with no players.
    /// </summary>
    private static bool PlayersLapCounted(RedlineHostView view)
    {
        var rows = view.Qualifying?.Rows ?? [];
        var players = rows.Where(r => view.Drivers.Any(d => d.Code == r.Code && d.Player is not null)).ToList();
        return (players.Count > 0 ? players : rows).Any(r => r.LastLapMs is not null);
    }

    /// <summary>A player's car has this lap's fastest time.</summary>
    private static bool PlayerPurple(RedlineHostView view) =>
        view.Qualifying?.Rows.Any(r => r.FastestLap && view.Drivers.Any(d => d.Code == r.Code && d.Player is not null)) == true;

    /// <summary>A sole leader who was not leading at the start of the round.</summary>
    private static bool NewLeader(RedlineHostView view) =>
        view.Drivers.Where(d => d.Position == 1).Take(2).ToList() is [{ PositionChange: > 0 }];
}
