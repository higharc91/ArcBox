using System.Numerics;

namespace Redline.Presentation;

/// <summary>
/// One arc-length-spaced point of the track centre line. <paramref name="Left"/> is the unit normal to the
/// left of travel, <paramref name="Curvature"/> the angle (radians) between the tangents
/// <see cref="TrackGeometry.Window"/> samples behind and ahead, and <paramref name="Turn"/> is +1 for a
/// right turn, -1 for a left turn and 0 when straight (x = east, y = south).
/// </summary>
public readonly record struct TrackSample(double S, Vector2 Position, Vector2 Tangent, Vector2 Left, double Height, double Curvature, int Turn);

/// <summary>Resampling, curvature, width profile and wall fold guard. Conventions match the browser mockup's cota3d.js.</summary>
public static class TrackGeometry
{
    public const double Step = 2.0;
    public const int Window = 15;
    public const double CornerThreshold = 0.22;

    private const int SubSteps = 64;
    private const double Alpha = 0.5;
    private const double Epsilon = 1e-4;

    /// <summary>
    /// Resamples the closed centripetal (alpha 0.5) Catmull-Rom curve through <see cref="CircuitData.Points"/> into
    /// round(curveLength / <see cref="Step"/>) samples spaced equally in arc length; the last sample wraps to the first.
    /// </summary>
    public static IReadOnlyList<TrackSample> Resample(CircuitData c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var n = c.Points.Count;
        var pts = new (double X, double Y)[n];
        for (var i = 0; i < n; i++)
            pts[i] = (c.Points[i].X, c.Points[i].Y);

        // Dense arc-length table: u = segment + fraction, cumulative length at each entry.
        var entries = n * SubSteps;
        var cumulative = new double[entries + 1];
        var prev = Curve(pts, 0);
        for (var k = 1; k <= entries; k++)
        {
            var cur = Curve(pts, (double)k / SubSteps);
            cumulative[k] = cumulative[k - 1] + Math.Sqrt(Sq(cur.X - prev.X) + Sq(cur.Y - prev.Y));
            prev = cur;
        }
        var curveLength = cumulative[entries];

        var count = (int)Math.Round(curveLength / Step);
        var spacing = curveLength / count;
        var pos = new (double X, double Y)[count];
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            var target = i * spacing;
            while (at < entries - 1 && cumulative[at + 1] < target)
                at++;
            var span = cumulative[at + 1] - cumulative[at];
            var f = span > 0 ? Math.Clamp((target - cumulative[at]) / span, 0, 1) : 0;
            pos[i] = Curve(pts, (at + f) / SubSteps);
        }

        var tan = new (double X, double Y)[count];
        for (var i = 0; i < count; i++)
        {
            var a = pos[(i - 1 + count) % count];
            var b = pos[(i + 1) % count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            tan[i] = len > 0 ? (dx / len, dy / len) : (1, 0);
        }

        var result = new TrackSample[count];
        for (var i = 0; i < count; i++)
        {
            var a = tan[((i - Window) % count + count) % count];
            var b = tan[(i + Window) % count];
            var cross = a.X * b.Y - a.Y * b.X;
            var dot = a.X * b.X + a.Y * b.Y;
            var s = i * spacing;
            var t = tan[i];
            result[i] = new TrackSample(
                s,
                new Vector2((float)pos[i].X, (float)pos[i].Y),
                new Vector2((float)t.X, (float)t.Y),
                new Vector2((float)t.Y, (float)-t.X),
                LapMath.Elevation(c, s),
                Math.Atan2(Math.Abs(cross), dot),
                Math.Sign(cross));
        }
        return result;
    }

    /// <summary>Track width in metres at <paramref name="s"/> metres along the lap: 15, widening to 24 through Turn 1.</summary>
    public static double Width(double s) => 15 + 9 * SmoothBump(s, 120, 200, 400, 470);

    public static bool IsCorner(TrackSample t) => t.Curvature > CornerThreshold;

    /// <summary>Approximate turning radius from curvature over the 2 × <see cref="Window"/> sample span.</summary>
    public static double LocalRadius(TrackSample t) => 2 * Window * Step / Math.Max(t.Curvature, 1e-3);

    /// <summary>
    /// False for a wall on the inside of a bend that would fold over itself. <paramref name="side"/> +1 is left, -1 is right;
    /// the wall is dropped when it is on the inner side and further from the centre line than 0.7 × the local radius.
    /// </summary>
    public static bool WallAllowed(TrackSample t, int side, double offsetFromCentre)
    {
        var inner = side != (t.Turn > 0 ? 1 : -1);
        return !(inner && offsetFromCentre > 0.7 * LocalRadius(t));
    }

    private static double SmoothBump(double s, double a, double b, double c, double d)
    {
        if (s <= a || s >= d) return 0;
        if (s < b) return Smooth((s - a) / (b - a));
        if (s > c) return 1 - Smooth((s - c) / (d - c));
        return 1;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Sq(double v) => v * v;

    /// <summary>Point on the closed centripetal Catmull-Rom curve at parameter u (segment index + fraction).</summary>
    private static (double X, double Y) Curve((double X, double Y)[] p, double u)
    {
        var n = p.Length;
        var seg = (int)Math.Floor(u);
        if (seg >= n) seg = n - 1;
        var f = u - seg;

        var p0 = p[((seg - 1) % n + n) % n];
        var p1 = p[seg % n];
        var p2 = p[(seg + 1) % n];
        var p3 = p[(seg + 2) % n];

        var t0 = 0.0;
        var t1 = t0 + Knot(p0, p1);
        var t2 = t1 + Knot(p1, p2);
        var t3 = t2 + Knot(p2, p3);
        var t = t1 + f * (t2 - t1);

        // Barry-Goldman pyramid.
        var a1 = Lerp(p0, p1, t0, t1, t);
        var a2 = Lerp(p1, p2, t1, t2, t);
        var a3 = Lerp(p2, p3, t2, t3, t);
        var b1 = Lerp(a1, a2, t0, t2, t);
        var b2 = Lerp(a2, a3, t1, t3, t);
        return Lerp(b1, b2, t1, t2, t);
    }

    private static double Knot((double X, double Y) a, (double X, double Y) b) =>
        Math.Max(Math.Pow(Sq(b.X - a.X) + Sq(b.Y - a.Y), Alpha * 0.5), Epsilon);

    private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double ta, double tb, double t)
    {
        var w = (t - ta) / (tb - ta);
        return (a.X + (b.X - a.X) * w, a.Y + (b.Y - a.Y) * w);
    }
}
