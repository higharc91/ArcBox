using Redline.Logic;

namespace Redline.Presentation.Audio;

/// <summary>What one engine voice should sound like now: <see cref="Synth.EngineVoice"/> rpm and load, and its level.</summary>
public readonly record struct EngineTarget(double Rpm, double Load, double GainDb);

/// <summary>
/// The engine bed (audio spec §3.3): a procedural engine on each of the camera's subjects (at most
/// <see cref="MaxVoices"/>), revving with the start lights on the grid, a low burble while the players think, and a roar
/// during the Reveal drive whose level scales with the round's biggest gain and whose revs follow the car's speed.
/// </summary>
public static class EngineMix
{
    public const int MaxVoices = 3;

    /// <summary>Speed (m/s along the race) at which a car's engine reaches <see cref="RevealTopRpm"/>.</summary>
    public const double FullSpeed = 150;

    public const double IdleRpm = 4200;
    public const double RevealBaseRpm = 6500;
    public const double RevealTopRpm = 12500;

    /// <summary>The engines while players choose, listen and answer: present but well under the voice.</summary>
    public const double ThinkingDb = -22;

    /// <summary>The cars on camera whose engines are heard, best first.</summary>
    public static IReadOnlyList<string> Subjects(Shot? shot, RedlineHostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var codes = view.Drivers.Select(d => d.Code).ToList();
        IEnumerable<string> picked = shot switch
        {
            HeliShot h => h.Subjects,
            // The car on camera and the one it is racing (next in the order, or the one ahead for the last car).
            TracksideShot t when codes.IndexOf(t.Subject) is var i and >= 0 =>
                [t.Subject, .. i + 1 < codes.Count ? [codes[i + 1]] : i > 0 ? [codes[i - 1]] : Array.Empty<string>()],
            TracksideShot t => [t.Subject],
            GridShot => codes,
            FinishShot => codes.Take(2),
            _ => [],
        };
        return [.. picked.Distinct().Take(MaxVoices)];
    }

    /// <summary>
    /// The engine for a car in <paramref name="phase"/> moving at <paramref name="speed"/> m/s (Reveal), with
    /// <paramref name="lightsLit"/> start lights on (Grid) and <paramref name="biggestGain"/> the round's largest gain
    /// (Reveal); null when the engines are off (Driver Select, Chequered).
    /// </summary>
    public static EngineTarget? Target(RacePhase phase, double speed, int lightsLit, int biggestGain)
    {
        switch (phase)
        {
            case RacePhase.Grid:
                {
                    var lit = Math.Clamp(lightsLit, 0, 5);
                    return new EngineTarget(IdleRpm + lit * 1400, 0.25 + 0.1 * lit, -18 + 1.6 * lit);
                }
            case RacePhase.Throttle or RacePhase.Reading or RacePhase.Question:
                return new EngineTarget(IdleRpm, 0.15, ThinkingDb);
            case RacePhase.Reveal:
                {
                    var pace = Math.Clamp(Math.Abs(speed) / FullSpeed, 0, 1);
                    var rpm = RevealBaseRpm + (RevealTopRpm - RevealBaseRpm) * Math.Pow(pace, 0.6);
                    // On the gas while the car still has real pace; lifting as it slows into its slot (overrun pops).
                    var load = Math.Abs(speed) > 6 ? 1.0 : 0.1;
                    var roarDb = -10 + 2 * Math.Clamp(biggestGain, 0, 4);
                    var presence = Math.Clamp(Math.Abs(speed) / 30, 0, 1);
                    return new EngineTarget(rpm, load, ThinkingDb + (roarDb - ThinkingDb) * presence);
                }
            default:
                return null;
        }
    }
}
