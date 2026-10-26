namespace Redline.Presentation;

/// <summary>
/// One moment of a car's Reveal animation. <see cref="Yaw"/> is radians added to the track heading (counter-clockwise
/// seen from above); <see cref="Smoke"/> is tyre-smoke emission 0..1; <see cref="Speed"/> is metres per second along the
/// race (negative when sliding back), for wheel spin.
/// </summary>
public readonly record struct MotionSample(double RaceMeters, double Lateral, double Yaw, double Smoke, double Speed);

/// <summary>
/// §4.8 Reveal motion: each car drives along the curve from its round-start slot to its new slot over
/// <see cref="Duration"/> seconds with a cubic ease-out, so bigger gains visibly drive faster. A spun-out car first spins
/// through <see cref="SpinTurns"/> × 360° (540°) over <see cref="SpinDuration"/> seconds, trailing smoke, then slides back
/// to its new slot facing backwards and turns round before it stops (a further 180°, so it ends facing forwards).
/// </summary>
public static class RevealMotion
{
    public const double Duration = 4;
    public const double SpinDuration = 1.6;
    public const double SpinTurns = 1.5;
    /// <summary>How far the spinning car carries on along the track before it starts sliding back.</summary>
    public const double SpinCarry = 14;
    /// <summary>Fraction of the slide back over which the car turns to face forwards again.</summary>
    public const double TurnRoundFraction = 0.35;
    /// <summary>Smoke keeps pouring this long after the spin, then fades out over the same time again.</summary>
    public const double SmokeTail = 0.4;

    public static double EaseOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1 - t;
        return 1 - u * u * u;
    }

    public static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The car's state <paramref name="seconds"/> after the Reveal started (clamped to the animation).</summary>
    public static MotionSample Sample(CarSlot from, CarSlot to, bool spunOut, double seconds)
    {
        seconds = Math.Max(0, seconds);
        return spunOut ? Spin(from, to, seconds) : Drive(from, to, seconds);
    }

    private static MotionSample Drive(CarSlot from, CarSlot to, double seconds)
    {
        var t = Math.Min(1, seconds / Duration);
        var s = Lerp(from.RaceMeters, to.RaceMeters, EaseOutCubic(t));
        var lateral = Lerp(from.Lateral, to.Lateral, SmoothStep(t));
        // d/dt of the ease-out: 3 (1 - t)² / Duration.
        var speed = t < 1 ? (to.RaceMeters - from.RaceMeters) * 3 * (1 - t) * (1 - t) / Duration : 0;
        return new MotionSample(s, lateral, 0, 0, speed);
    }

    private static MotionSample Spin(CarSlot from, CarSlot to, double seconds)
    {
        var smoke = seconds <= SpinDuration + SmokeTail
            ? 1
            : Math.Max(0, 1 - (seconds - SpinDuration - SmokeTail) / SmokeTail);

        if (seconds < SpinDuration)
        {
            // Quadratic ease-out: the car snaps round fast and scrubs off rotation as it slows.
            var p = seconds / SpinDuration;
            var e = 1 - (1 - p) * (1 - p);
            var carryLateral = from.Lateral + (to.Lateral - from.Lateral) * 0.3 * e;
            var speed = SpinCarry * 2 * (1 - p) / SpinDuration;
            return new MotionSample(from.RaceMeters + SpinCarry * e, carryLateral, SpinTurns * 2 * Math.PI * e, smoke, speed);
        }

        var slide = Math.Min(1, (seconds - SpinDuration) / (Duration - SpinDuration));
        var start = from.RaceMeters + SpinCarry;
        var k = SmoothStep(slide);
        var s = Lerp(start, to.RaceMeters, k);
        var lateral = Lerp(from.Lateral + (to.Lateral - from.Lateral) * 0.3, to.Lateral, k);
        var turn = SmoothStep((slide - (1 - TurnRoundFraction)) / TurnRoundFraction);
        var yaw = SpinTurns * 2 * Math.PI + Math.PI * turn;
        // d/dt of the smoothstep: 6 k (1 - k) / span.
        var slideSpeed = slide < 1 ? (to.RaceMeters - start) * 6 * slide * (1 - slide) / (Duration - SpinDuration) : 0;
        return new MotionSample(s, lateral, yaw, smoke, slideSpeed);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
