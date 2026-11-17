using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Fx;

namespace Meridian.Presentation.Audio;

/// <summary>A music request: a track, and for a sting the track it runs into.</summary>
public sealed record MusicCue(string Id, string? Then = null);

/// <summary>A sound at a moment on a beat's timeline.</summary>
public sealed record SoundCue(string Id, double At);

/// <summary>
/// What Meridian sounds like (#338; M5 plan, Task 7): the music for each phase, the sounds for each beat step, footsteps
/// and the last ten seconds' tick. Like Tiller's lines and the board effects, everything here comes from the host view
/// (what everyone may see) and its beats, so no sound can tell the room a secret. Ids are the art contract's
/// <c>audio</c> ids; one with no file is skipped silently.
/// </summary>
public static class SoundPlan
{
    /// <summary>The tick counts down the last this-many seconds of a public decision.</summary>
    public const int TickFrom = 10;

    /// <summary>A footstep every this-many seconds while anyone walks (crew walk 3.5 cells a second).</summary>
    public const double FootstepEvery = 0.3;

    /// <summary>
    /// The music for what's on screen: an ambience loop while boarding and exploring; at the Breach a sting that runs into
    /// the endgame's tension loop; at the Debrief, the crew's win or loss. Null leaves the music as it is.
    /// </summary>
    public static MusicCue? Music(MeridianHostView view, Beat? current)
    {
        if (view.Scenario is { Reveal: not null, CrewWon: var won })
        {
            return new MusicCue(won == true ? "music.debriefWon" : "music.debriefLost");
        }
        if (view.Phase == "endgame")
        {
            return new MusicCue("music.endgame");
        }
        // From the Breach on (the scenario panel exists only once it has fired) until the endgame begins: the sting, running
        // into the endgame's loop. The Breach beat can end before the phase moves on, so the phase alone isn't enough.
        if (current is { Kind: "breach", Short: false } || view.Phase == "briefing" || (view.Scenario is not null && view.Phase == "explore"))
        {
            return new MusicCue("music.breach", Then: "music.endgame");
        }
        return view.Phase is "boarding" or "explore" ? new MusicCue("music.explore") : null;
    }

    /// <summary>Every sound of a beat, by step. A brief beat (a TV that has just connected) shows, never replays: none.</summary>
    public static EqArray<SoundCue> For(Beat beat)
    {
        if (beat.Short)
        {
            return [];
        }
        var now = beat.Now;
        double At(string step)
        {
            var at = 0.0;
            foreach (var s in beat.Steps)
            {
                if (s.Name == step)
                {
                    return at;
                }
                at += s.Seconds;
            }
            return 0;
        }
        return now.Kind switch
        {
            "reveal" => [new("sfx.reveal", At("drop")), new("sfx.hatchOpen", At("hatches"))],
            "sealed" => [new("sfx.hatchSeal", 0)],
            "card" => Card(now, At("hold")),
            "roll" => [new("sfx.diceRoll", 0), new("sfx.diceSettle", At("land"))],
            "fight" => [new("sfx.diceRoll", At("dice")), .. Outcome(now.Outcome) is { } hit ? [new SoundCue(hit, At("tick"))] : Array.Empty<SoundCue>()],
            "breach" => [new("sfx.alarm", 0)],
            "death" => [new("sfx.death", 0), new("sfx.echo", At("hold"))],
            _ => [],
        };
    }

    private static EqArray<SoundCue> Card(Presented now, double hold)
    {
        var deal = now.Deck switch
        {
            CardKind.Incident => "sfx.cardIncident",
            CardKind.Salvage => "sfx.cardSalvage",
            CardKind.Anomaly => "sfx.cardAnomaly",
            _ => null,
        };
        var cues = new List<SoundCue>();
        if (deal is not null)
        {
            cues.Add(new(deal, 0));
        }
        // A hull breach or a gravity drop vents air, as the board's pressure effect shows.
        if (now.Card is { } card && EffectPlan.IncidentFx.GetValueOrDefault(card) == "pressure")
        {
            cues.Add(new("sfx.vent", hold));
        }
        return [.. cues];
    }

    private static string? Outcome(string? outcome) => outcome switch
    {
        "hit" or "stunned" => "sfx.hit",
        "staggered" => "sfx.stagger",
        "tie" => "sfx.miss",
        _ => null,
    };

    /// <summary>The sounds whose moment falls in [<paramref name="from"/>, <paramref name="to"/>) on the beat's timeline.</summary>
    public static IEnumerable<SoundCue> Due(Beat beat, double from, double to)
        => to <= from ? [] : For(beat).Where(c => c.At >= from && c.At < to);

    /// <summary>
    /// Whether the tick sounds between <paramref name="from"/> and <paramref name="to"/> seconds after a view that said
    /// <paramref name="turnEndsIn"/>: once as each of the last ten seconds begins.
    /// </summary>
    public static bool Tick(int? turnEndsIn, double from, double to)
        => turnEndsIn is { } ends && Enumerable.Range(1, TickFrom).Any(k => ends - from > k && ends - to <= k);

    /// <summary>The file for a sound or a track, or null: a missing sound is skipped, never an error.</summary>
    public static ArtEntry? File(MeridianArt art, string id) => art.Entry($"audio/{id}");
}
