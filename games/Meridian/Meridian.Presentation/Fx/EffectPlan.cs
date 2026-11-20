using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;

namespace Meridian.Presentation.Fx;

/// <summary>Where an effect plays.</summary>
public enum FxPlace
{
    /// <summary>Over the whole screen, under the Breach's words.</summary>
    Screen,
    /// <summary>Centred on a compartment.</summary>
    Compartment,
    /// <summary>On each of a compartment's hatches (open ones for an opening, sealed ones for a weld).</summary>
    Hatches,
    /// <summary>On a crew member (by seat) or a monster (by token id), wherever they are drawn.</summary>
    Actor,
}

/// <summary>
/// One effect in a beat: which (an <c>fx</c> id from the art contract), where, and when on the beat's timeline. A looping
/// effect repeats until <see cref="End"/>; any other plays its strip once and holds its last frame.
/// </summary>
public sealed record Cue(string Fx, FxPlace Place, string? Where, double Start, double Seconds, bool Loop = false)
{
    public double End => Start + Seconds;

    public bool Plays(double t) => t >= Start && t < End;
}

/// <summary>
/// The board effects each beat plays (spec §5.5; M5 plan, Task 6). The scene decides nothing: it asks which cues play at
/// the beat's elapsed time, and draws them. Every cue sits inside its beat's timeline, so effects never hold a beat up
/// and the beat timing tests still hold. Effects come only from presented (public) events, like Tiller's lines.
/// </summary>
public static class EffectPlan
{
    /// <summary>
    /// The Incidents whose narration has a board effect, by card id. Venting, rot and pressure loss also belong to the
    /// hazard scenarios (Captain on Deck, Hull Rot, Vent Cycle), which will cue them from their own notices.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> IncidentFx = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sparking_conduit"] = "sparks",
        ["jammed_bulkhead"] = "sparks",
        ["hull_groan"] = "pressure",
        ["gravity_flicker"] = "pressure",
    };

    /// <summary>Every cue of a beat, in order. A brief beat (a TV that has just connected) shows, never replays: none.</summary>
    public static EqArray<Cue> For(Beat beat)
    {
        if (beat.Short)
        {
            return [];
        }
        var now = beat.Now;
        // A step's start and length on the timeline; to the end of the beat from a step with `rest`.
        (double Start, double Seconds) Step(string name, bool rest = false)
        {
            var start = 0.0;
            foreach (var step in beat.Steps)
            {
                if (step.Name == name)
                {
                    return (start, rest ? beat.Seconds - start : step.Seconds);
                }
                start += step.Seconds;
            }
            return (0, beat.Seconds);
        }
        Cue At(string fx, FxPlace place, string? where, (double Start, double Seconds) span, bool loop = false)
            => new(fx, place, where, span.Start, span.Seconds, loop);

        var whole = (0.0, beat.Seconds);
        return now.Kind switch
        {
            "reveal" when now.Compartment is { } room =>
                [At("reveal", FxPlace.Compartment, room, Step("drop")), At("hatchOpen", FxPlace.Hatches, room, Step("hatches", rest: true))],
            "sealed" when now.Compartment is { } room => [At("hatchSeal", FxPlace.Hatches, room, Step("weld", rest: true))],
            "unsealed" when now.Compartment is { } room => [At("hatchOpen", FxPlace.Hatches, room, Step("cut", rest: true))],
            // A scenario's hazard (venting, rot, pressure, sparks) on the compartment its notice names.
            "notice" when now.Fx is { } hazard && now.Compartment is { } room => [At(hazard, FxPlace.Compartment, room, whole)],
            "fight" => Fight(now, Step("tick", rest: true), At),
            "breach" => [At("breachAlarm", FxPlace.Screen, null, whole, loop: true), At("breachDistortion", FxPlace.Screen, null, Step("in"))],
            "death" => [At("echo", FxPlace.Actor, now.Seat.Value, whole)],
            "card" when now.Card is { } card && IncidentFx.TryGetValue(card, out var fx) => [At(fx, FxPlace.Actor, now.Seat.Value, whole)],
            _ => [],
        };
    }

    private static EqArray<Cue> Fight(Presented now, (double, double) tick, Func<string, FxPlace, string?, (double, double), bool, Cue> at)
        => now.Outcome switch
        {
            // A hit lands on a crew member (a monster is stunned instead), who loses physical steps for it.
            "hit" => [at("hit", FxPlace.Actor, now.Target, tick, false), at("traitLoss", FxPlace.Actor, now.Target, tick, false)],
            "stunned" => [at("hit", FxPlace.Actor, now.Target, tick, false), at("stun", FxPlace.Actor, now.Target, tick, true)],
            "staggered" => [at("stagger", FxPlace.Actor, now.Attacker, tick, false)],
            "tie" => [at("miss", FxPlace.Actor, now.Target, tick, false)],
            _ => [],
        };

    /// <summary>The cues playing at <paramref name="elapsed"/> seconds into the beat.</summary>
    public static IEnumerable<Cue> Playing(Beat beat, double elapsed) => For(beat).Where(c => c.Plays(elapsed));

    /// <summary>
    /// An effect's art. Each <c>fx</c> id holds <c>full</c> and <c>reduced</c> entries (or a bare entry, the full one).
    /// With reduced effects on (the TV's <c>ReducedBattleEffects</c>) only the reduced entry plays, with no flash and no
    /// shake; with none drawn, nothing plays. A full effect never stands in for a reduced one.
    /// </summary>
    public static ArtEntry? Art(MeridianArt art, string fx, bool reduced)
        => reduced ? art.Entry($"fx/{fx}/reduced") : art.Entry($"fx/{fx}/full") ?? art.Entry($"fx/{fx}");

    /// <summary>The strip's frame at <paramref name="t"/> seconds into its cue: looping, or once and held.</summary>
    public static int Frame(ArtEntry strip, Cue cue, double t)
    {
        var frame = (int)(Math.Max(0, t - cue.Start) * Math.Max(1, strip.Fps));
        return cue.Loop && strip.Loop ? frame % strip.Frames : Math.Min(frame, strip.Frames - 1);
    }
}
