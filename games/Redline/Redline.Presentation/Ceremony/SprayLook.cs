using System.Numerics;
using Redline.Logic;

namespace Redline.Presentation.Ceremony;

/// <summary>
/// How one champagne spray looks and sounds for its strength (podium spec §5: size, velocity and duration scale with
/// strength). A full bottle (1.0) roars out for about three seconds and carries ten metres; a cork popped with no
/// pressure (<see cref="Celebration.MinSprayStrength"/>) fizzes out in well under a second.
/// </summary>
/// <param name="Strength">The spray's strength, clamped to <see cref="Celebration.MinSprayStrength"/>..1.</param>
/// <param name="Seconds">How long the bottle keeps spraying.</param>
/// <param name="Lifetime">How long each blob of foam flies.</param>
/// <param name="Rate">Foam blobs emitted per second.</param>
/// <param name="SpeedMin">Slowest blob, metres per second.</param>
/// <param name="SpeedMax">Fastest blob, metres per second.</param>
/// <param name="Spread">Half-angle of the jet, degrees (weaker sprays dribble wider).</param>
/// <param name="Size">A foam blob's size, metres.</param>
/// <param name="CorkSpeed">The cork's launch speed, metres per second.</param>
/// <param name="Flash">The pop flash's light energy.</param>
/// <param name="HissDb">The spray hiss's level, dB relative to the clip.</param>
/// <param name="HissPitch">The spray hiss's playback rate (a stronger spray hisses higher).</param>
public readonly record struct SprayLook(
    double Strength, double Seconds, double Lifetime, double Rate, double SpeedMin, double SpeedMax, double Spread,
    double Size, double CorkSpeed, double Flash, double HissDb, double HissPitch)
{
    /// <summary>The most foam blobs alive at once from one spray (for sizing particle buffers).</summary>
    public const int MaxBlobs = 1100;

    /// <summary>Foam blobs alive at once for this spray, never more than <see cref="MaxBlobs"/>.</summary>
    public int Blobs => (int)Math.Min(MaxBlobs, Math.Ceiling(Rate * Lifetime));

    public static SprayLook For(double strength)
    {
        var s = double.IsFinite(strength) ? Math.Clamp(strength, Celebration.MinSprayStrength, 1) : Celebration.MinSprayStrength;
        var speedMax = 3.6 + (8.4 * s);
        var lifetime = 0.85 + (1.05 * s);
        return new SprayLook(
            Strength: s,
            Seconds: 0.55 + (2.65 * s),
            Lifetime: lifetime,
            Rate: Math.Min(MaxBlobs / lifetime, 140 + (520 * s)),
            SpeedMin: speedMax * 0.55,
            SpeedMax: speedMax,
            Spread: 7 + (7 * (1 - s)),
            Size: 0.15 + (0.21 * s),
            CorkSpeed: 7 + (11 * s),
            Flash: 1.5 + (6.5 * s),
            HissDb: -13 + (11 * s),
            HissPitch: 0.86 + (0.28 * s));
    }

    /// <summary>
    /// The jet's direction (local <see cref="PodiumLayout"/> frame, unit length) <paramref name="seconds"/> after the cork
    /// popped: up and out towards the camera and the crowd, the outer steps turned in towards the winner, every bottle
    /// sweeping from side to side as it is waved about.
    /// </summary>
    public static Vector3 Aim(int step, double seconds)
    {
        var inward = step switch { 2 => 0.7f, 3 => -0.7f, _ => 0f };
        // Each step waves at its own pace and phase, so the three jets never move in lock-step.
        var (rate, phase) = step switch { 1 => (1.6, 0.0), 2 => (1.25, 1.9), _ => (1.45, 3.7) };
        var sweep = (float)((step == 1 ? 0.6 : 0.45) * Math.Sin((seconds * rate * 2 * Math.PI) + phase));
        var lift = 0.85f + (float)(0.15 * Math.Sin((seconds * 0.9 * 2 * Math.PI) + phase));
        return Vector3.Normalize(new Vector3(inward + sweep, lift, 0.85f));
    }
}
