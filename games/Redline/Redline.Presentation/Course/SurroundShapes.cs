using System.Numerics;

namespace Redline.Presentation.Course;

/// <summary>A circle on the ground plane (x east, y = z south) where nothing new may stand: a landmark or a tree crown.</summary>
public readonly record struct KeepOut(Vector2 Centre, float Radius);

/// <summary>
/// An oriented rectangle on the ground plane (x east, y = z south): <see cref="Axis"/> is the unit direction of its
/// length, <see cref="Across"/> its width (Axis turned a quarter turn), half extents along each.
/// </summary>
public readonly record struct Obb(Vector2 Centre, Vector2 Axis, float HalfLength, float HalfWidth)
{
    public Vector2 Across => new(-Axis.Y, Axis.X);

    /// <summary>Radius of the circle round the centre that holds the whole rectangle.</summary>
    public float Reach => MathF.Sqrt((HalfLength * HalfLength) + (HalfWidth * HalfWidth));

    /// <summary>Yaw (radians, counter-clockwise seen from above in Godot) that turns local +X onto <see cref="Axis"/>.</summary>
    public float Yaw => MathF.Atan2(-Axis.Y, Axis.X);

    /// <summary>The point <paramref name="along"/> metres along the axis and <paramref name="across"/> metres across.</summary>
    public Vector2 At(float along, float across) => Centre + (Axis * along) + (Across * across);

    public IReadOnlyList<Vector2> Corners =>
        [At(-HalfLength, -HalfWidth), At(HalfLength, -HalfWidth), At(HalfLength, HalfWidth), At(-HalfLength, HalfWidth)];

    /// <summary>The same rectangle grown by <paramref name="margin"/> on every side.</summary>
    public Obb Grown(float margin) => this with { HalfLength = HalfLength + margin, HalfWidth = HalfWidth + margin };

    /// <summary>Distance from <paramref name="p"/> to the rectangle (0 inside).</summary>
    public float Distance(Vector2 p)
    {
        var d = p - Centre;
        var u = MathF.Max(MathF.Abs(Vector2.Dot(d, Axis)) - HalfLength, 0);
        var v = MathF.Max(MathF.Abs(Vector2.Dot(d, Across)) - HalfWidth, 0);
        return MathF.Sqrt((u * u) + (v * v));
    }

    public bool Contains(Vector2 p) => Distance(p) <= 0;

    /// <summary>True when the two rectangles overlap or come within <paramref name="margin"/> of each other.</summary>
    public bool Overlaps(Obb other, float margin = 0)
    {
        var a = Grown(margin / 2);
        var b = other.Grown(margin / 2);
        foreach (var axis in new[] { a.Axis, a.Across, b.Axis, b.Across })
        {
            var (a0, a1) = a.Project(axis);
            var (b0, b1) = b.Project(axis);
            if (a1 < b0 || b1 < a0)
                return false;
        }
        return true;
    }

    /// <summary>Distance from the segment <paramref name="p"/>–<paramref name="q"/> to the rectangle (0 when they touch).</summary>
    public float Distance(Vector2 p, Vector2 q)
    {
        if (Contains(p) || Contains(q))
            return 0;
        var c = Corners;
        for (var i = 0; i < 4; i++)
            if (Shapes.SegmentsCross(p, q, c[i], c[(i + 1) % 4]))
                return 0;
        var best = MathF.Min(Distance(p), Distance(q));
        foreach (var k in c)
            best = MathF.Min(best, Shapes.SegmentDistance(k, p, q));
        return best;
    }

    private (float Min, float Max) Project(Vector2 axis)
    {
        var c = Vector2.Dot(Centre, axis);
        var r = (HalfLength * MathF.Abs(Vector2.Dot(Axis, axis))) + (HalfWidth * MathF.Abs(Vector2.Dot(Across, axis)));
        return (c - r, c + r);
    }
}

/// <summary>Small plane-geometry helpers for the surroundings planner.</summary>
public static class Shapes
{
    /// <summary>Distance from <paramref name="p"/> to the segment <paramref name="a"/>–<paramref name="b"/>.</summary>
    public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0, 1) : 0;
        return Vector2.Distance(p, a + (ab * t));
    }

    /// <summary>True when the segments a–b and c–d properly cross.</summary>
    public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 o, Vector2 p, Vector2 q) => ((p.X - o.X) * (q.Y - o.Y)) - ((p.Y - o.Y) * (q.X - o.X));
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>Even-odd point-in-polygon test (the circuit's centre line: true in the infield).</summary>
    public static bool Inside(IReadOnlyList<Vector2> polygon, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>The unit vector at <paramref name="angle"/> radians from +x towards +y (east towards south).</summary>
    public static Vector2 Dir(double angle) => new((float)Math.Cos(angle), (float)Math.Sin(angle));
}
