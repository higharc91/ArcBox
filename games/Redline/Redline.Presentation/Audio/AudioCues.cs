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
    public const string Screech = "screech";
    public const string Crunch = "crunch";
    public const string Cheer = "cheer";
    public const string Fanfare = "fanfare";
    /// <summary>The crowd murmur under the race (a loop).</summary>
    public const string CrowdBed = "crowd-bed";

    /// <summary>Countdown ticks sound at this many seconds left and every second after, down to 1.</summary>
    public const int CountdownFrom = 5;

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
                case RacePhase.Question when Humans(view, DriverStatus.Answered) > Humans(previous!, DriverStatus.Answered):
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
            case RacePhase.Chequered:
                cues.Add(Fanfare);
                cues.Add(Cheer);
                break;
        }
        return cues;
    }

    /// <summary>
    /// True when the question should be read out now: on entering Reading, or on entering Question straight from the
    /// same round's Throttle (Reading switched off). Never for a repeated view or a TV that joins mid-question.
    /// </summary>
    public static bool ReadsQuestion(RedlineHostView view, RedlineHostView? previous)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!IsNewPhase(view, previous) || view.Question?.Text is not { Length: > 0 })
        {
            return false;
        }
        return view.Phase == RacePhase.Reading
               || (view.Phase == RacePhase.Question && previous is { Phase: RacePhase.Throttle } p && p.Round == view.Round);
    }

    /// <summary>Countdown ticks due as the clock runs from <paramref name="before"/> to <paramref name="after"/> seconds left.</summary>
    public static int CountdownTicks(RacePhase phase, double before, double after)
    {
        if (phase is not (RacePhase.Throttle or RacePhase.Question) || after >= before)
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

    /// <summary>A sole leader who was not leading at the start of the round.</summary>
    private static bool NewLeader(RedlineHostView view) =>
        view.Drivers.Where(d => d.Position == 1).Take(2).ToList() is [{ PositionChange: > 0 }];
}
