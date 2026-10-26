using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation;

/// <summary>A camera: where it is, what it looks at, and its vertical field of view in degrees.</summary>
public readonly record struct CameraPose(Vector3 Eye, Vector3 Target, float Fov)
{
    public static CameraPose Lerp(CameraPose a, CameraPose b, float t) => new(
        Vector3.Lerp(a.Eye, b.Eye, t), Vector3.Lerp(a.Target, b.Target, t), a.Fov + (b.Fov - a.Fov) * t);
}

/// <summary>
/// §4.9 shot geometry: turns a <see cref="Shot"/> plus live car poses into a <see cref="CameraPose"/>. Pure maths in the
/// mesh convention (Y up, metres), so the Godot rig only blends and applies the result. <c>ground</c> callbacks return
/// the terrain height at (x, z); cameras are kept <see cref="Clearance"/> above it.
/// </summary>
public static class ShotFraming
{
    public const float Clearance = 2.5f;

    public const float HeliDistance = 60;
    public const float HeliHeight = 25;
    public const float HeliFov = 22;
    /// <summary>Front-quarter angle of the helicopter from straight out to the side, in degrees.</summary>
    public const float HeliQuarter = 35;
    /// <summary>Slow orbit, degrees per second.</summary>
    public const float HeliOrbit = 2.5f;

    public const float TracksideAhead = 9;
    public const float TracksideOut = 5;
    public const float TracksideHeight = 1.2f;
    public const float TracksideFov = 36;

    public const float GridFov = 36;
    public const float LineupRadius = 58;
    /// <summary>High enough to look over the pit building's roof (10 m up, 29 m out) at the middle of the grid.</summary>
    public const float LineupHeight = 24;
    public const float LineupOrbit = 3.5f;

    /// <summary>High behind the grid, looking down the straight towards the gantry and the climb to Turn 1.</summary>
    public static CameraPose Grid(IReadOnlyList<TrackSample> samples)
    {
        var eye = TrackMeshBuilder.TrackPoint(samples, -Formation.PoleSetback - 215, 1.5) + new Vector3(0, 17, 0);
        var target = TrackMeshBuilder.TrackPoint(samples, -70, 0);
        return new CameraPose(eye, target, GridFov);
    }

    /// <summary>
    /// Helicopter framing <paramref name="subjects"/>: 25 m up and 60 m out from their midpoint on <paramref name="side"/>
    /// (+1 left of travel, -1 right), a front quarter, slowly orbiting with <paramref name="seconds"/>. Backs off (keeping
    /// the same elevation angle) until every subject fits the frame with margin.
    /// </summary>
    public static CameraPose Heli(IReadOnlyList<Vector3> subjects, Vector3 forward, int side, double seconds, Func<float, float, float>? ground = null)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        if (subjects.Count == 0)
            throw new ArgumentException("A helicopter shot needs at least one subject.", nameof(subjects));

        var target = subjects.Aggregate(Vector3.Zero, (a, p) => a + p) / subjects.Count;
        var spread = subjects.Max(p => Vector3.Distance(p, target));
        var halfFov = HeliFov / 2 * MathF.PI / 180;
        // Fit the spread (plus a car length of margin) inside the 16:9 frame's half-width at the base distance.
        var halfWidth = MathF.Tan(halfFov) * 16f / 9f * HeliDistance;
        var scale = MathF.Max(1, (spread + 5) / (halfWidth * 0.8f));
        var distance = HeliDistance * scale;
        var height = HeliHeight * scale;

        var flat = Flat(forward);
        var left = new Vector3(flat.Z, 0, -flat.X);
        var out_ = left * (side >= 0 ? 1 : -1);
        var angle = (HeliQuarter + HeliOrbit * (float)seconds) * MathF.PI / 180 * (side >= 0 ? -1 : 1);
        var dir = Rotate(out_, angle);
        var eye = target + dir * distance + new Vector3(0, height, 0);
        return new CameraPose(Lift(eye, ground), target + new Vector3(0, 0.6f, 0), HeliFov);
    }

    /// <summary>
    /// Low front-quarter camera riding with the car: 9 m ahead, 5 m out on <paramref name="side"/> (at least 1.2 m beyond the
    /// asphalt edge), 1.2 m up.
    /// </summary>
    public static CameraPose Trackside(IReadOnlyList<TrackSample> samples, double raceMeters, double lateral, Vector3 carPosition, int side,
        Func<float, float, float>? ground = null)
    {
        // 5 m out from the car, but never on the asphalt itself, where it could meet a car in the row ahead.
        var sign = side >= 0 ? 1 : -1;
        var edge = TrackGeometry.Width(Wrap(raceMeters + TracksideAhead, samples)) / 2 + 1.2;
        var out_ = sign * Math.Max(sign * lateral + TracksideOut, edge);
        var eye = TrackMeshBuilder.TrackPoint(samples, raceMeters + TracksideAhead, out_) + new Vector3(0, TracksideHeight, 0);
        return new CameraPose(Lift(eye, ground, 0.6f), carPosition + new Vector3(0, 0.45f, 0), TracksideFov);
    }

    /// <summary>
    /// Driver Select: a slow sweep round the grid on the pit side (the main grandstand stands on the other), from the front
    /// quarter to the rear quarter and back, about <see cref="LineupRadius"/> out and <see cref="LineupHeight"/> up.
    /// </summary>
    public static CameraPose LineupOrbitPose(IReadOnlyList<TrackSample> samples, double seconds, Func<float, float, float>? ground = null)
    {
        var centre = TrackPose.At(samples, -Formation.PoleSetback - 84, 0);
        // Ping-pong between 25° and 155° from straight ahead, measured towards the left (pit) side.
        var span = 130.0;
        var swept = seconds * LineupOrbit % (2 * span);
        var deg = 25 + (swept < span ? swept : 2 * span - swept);
        var (sin, cos) = MathF.SinCos((float)(deg * Math.PI / 180));
        var flatFwd = Flat(centre.Forward);
        var flatLeft = new Vector3(flatFwd.Z, 0, -flatFwd.X);
        var eye = centre.Position + (flatFwd * cos + flatLeft * sin) * LineupRadius + new Vector3(0, LineupHeight, 0);
        return new CameraPose(Lift(eye, ground), centre.Position + new Vector3(0, 1, 0), 40);
    }

    /// <summary>
    /// Driver Select hold on a newly claimed car: a close front quarter on the outside of its grid column
    /// (<paramref name="side"/> +1 left, -1 right), so the camera never sits in the car alongside.
    /// </summary>
    public static CameraPose LineupHold(TrackPose car, int side, Func<float, float, float>? ground = null)
    {
        var eye = car.Position + car.Forward * 8.6f + car.Left * (5.4f * (side >= 0 ? 1 : -1)) + car.Up * 1.25f;
        return new CameraPose(Lift(eye, ground, 0.6f), car.Position + car.Forward * 0.2f + car.Up * 0.35f, 28);
    }

    /// <summary>Chequered flag: beyond the line on the pit side, looking back past the gantry at the finishers.</summary>
    public static CameraPose FinishLine(IReadOnlyList<TrackSample> samples)
    {
        var eye = TrackMeshBuilder.TrackPoint(samples, 48, 15) + new Vector3(0, 9, 0);
        var target = TrackMeshBuilder.TrackPoint(samples, -12, 0) + new Vector3(0, 1.5f, 0);
        return new CameraPose(eye, target, 38);
    }

    private static double Wrap(double s, IReadOnlyList<TrackSample> samples)
    {
        var lap = samples.Count * (samples[1].S - samples[0].S);
        s %= lap;
        return s < 0 ? s + lap : s;
    }

    private static Vector3 Flat(Vector3 v)
    {
        var f = new Vector3(v.X, 0, v.Z);
        return f.LengthSquared() > 1e-8f ? Vector3.Normalize(f) : Vector3.UnitZ;
    }

    /// <summary>Rotates a horizontal vector about +Y (counter-clockwise seen from above for positive angles).</summary>
    private static Vector3 Rotate(Vector3 v, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector3(v.X * cos + v.Z * sin, 0, -v.X * sin + v.Z * cos);
    }

    private static Vector3 Lift(Vector3 eye, Func<float, float, float>? ground, float clearance = Clearance)
    {
        if (ground is null)
            return eye;
        var floor = ground(eye.X, eye.Z) + clearance;
        return eye.Y < floor ? eye with { Y = floor } : eye;
    }
}
