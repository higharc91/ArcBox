using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Course;

/// <summary>
/// The far land beyond the terrain grid, out to <see cref="Reach"/> metres from the circuit's middle in every direction:
/// a coarse ring of gentle Texas hills that starts just under the terrain's edge (so no step or gap shows there) and
/// drops away with distance, under the fog. The skyline stands on it.
/// </summary>
public sealed class SurroundHorizon
{
    public const float Reach = 8500;

    /// <summary>Grid spacing along the terrain's edge (and the band that tucks under it).</summary>
    public const float EdgeStep = 64;

    /// <summary>The far land stays at least this far below the terrain where they meet.</summary>
    public const float Tuck = 1.5f;

    private static readonly Vector4 Field = MeshOps.Hex("#8c8a50");

    private readonly TerrainSampler _g;
    private readonly Vector2 _centre;
    private readonly float _base;

    public SurroundHorizon(TerrainSampler ground, IReadOnlyList<TrackSample> samples)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(samples);
        _g = ground;
        _centre = new Vector2((ground.X0 + ground.X1) / 2, (ground.Z0 + ground.Z1) / 2);
        _base = (float)samples.Average(t => t.Height);
    }

    /// <summary>How far the land has dropped (metres) at <paramref name="beyond"/> metres past the terrain's edge.</summary>
    public static float FarDrop(float beyond) => (beyond * beyond / (2 * 6_371_000f)) + (6 * Smooth(beyond / 4000));

    /// <summary>Far-land height at <paramref name="p"/>; on and inside the terrain's edge, at least <see cref="Tuck"/> below it.</summary>
    public float Height(Vector2 p)
    {
        var beyond = Beyond(p);
        var edge = EdgeMin(p) - Tuck;
        var hills = _base + 1.5f
            + (14 * MathF.Sin((p.X * 0.00071f) + 0.4f) * MathF.Cos((p.Y * 0.00063f) - 1.2f))
            + (6 * MathF.Sin(((p.X - p.Y) * 0.0019f) + 2.3f));
        var t = Smooth(beyond / 900);
        // Never above the edge inside the tuck band; rising into the hills further out.
        var h = edge + ((hills - edge) * t);
        return h + Ridges(p - _centre, beyond) - FarDrop(beyond);
    }

    /// <summary>
    /// The ring mesh: a grid whose lines include the terrain's edges (spaced <see cref="EdgeStep"/> along them, widening
    /// outwards), every cell outside the terrain plus one band of cells tucked under its edge. UV = (x, z) metres.
    /// </summary>
    public MeshData Build()
    {
        var xs = Lines(_g.X0, _g.X1, _centre.X);
        var zs = Lines(_g.Z0, _g.Z1, _centre.Y);
        var m = new MeshData();
        var index = new int[xs.Count, zs.Count];
        for (var j = 0; j < zs.Count; j++)
            for (var i = 0; i < xs.Count; i++)
            {
                var p = new Vector2(xs[i], zs[j]);
                index[i, j] = m.AddVertex(new Vector3(p.X, Height(p), p.Y), p, Field);
            }
        for (var j = 0; j + 1 < zs.Count; j++)
            for (var i = 0; i + 1 < xs.Count; i++)
            {
                // Skip cells well inside the terrain (keep the band of one EdgeStep tucked under its edge).
                var inner = xs[i] >= _g.X0 + EdgeStep - 0.5f && xs[i + 1] <= _g.X1 - EdgeStep + 0.5f
                    && zs[j] >= _g.Z0 + EdgeStep - 0.5f && zs[j + 1] <= _g.Z1 - EdgeStep + 0.5f;
                if (inner)
                    continue;
                m.AddQuad(index[i, j], index[i + 1, j], index[i + 1, j + 1], index[i, j + 1]);
            }
        m.ComputeNormals();
        return m;
    }

    /// <summary>
    /// Distant ridgelines, so the horizon isn't ruler-straight: rising from 0.8 km past the terrain's edge to full
    /// height (about 80-200 m) by 5 km, varying round the compass, higher to the west (the Hill Country), and low towards
    /// Austin so the skyline stays in view.
    /// </summary>
    public static float Ridges(Vector2 rel, float beyond)
    {
        if (beyond <= 800)
            return 0;
        var dir = Vector2.Normalize(rel);
        var theta = MathF.Atan2(dir.Y, dir.X);
        var ridge = (80 * (0.6f + (0.4f * MathF.Sin((3 * theta) + 1.1f) * MathF.Cos((5 * theta) - 0.4f))))
            + (28 * MathF.Sin((11 * theta) + 0.3f)) + (14 * MathF.Sin((23 * theta) + 2)) + (6 * MathF.Sin((41 * theta) + 0.7f));
        ridge *= 1 + (0.6f * MathF.Max(0, -dir.X));
        var open = 1 - (0.92f * Smooth((Vector2.Dot(dir, Surroundings.AustinDirection) - 0.8f) / 0.15f));
        return MathF.Max(0, ridge) * open * Smooth((beyond - 800) / 4200);
    }

    /// <summary>Metres past the terrain's rectangle (0 on or inside it).</summary>
    public float Beyond(Vector2 p)
    {
        var dx = MathF.Max(MathF.Max(_g.X0 - p.X, p.X - _g.X1), 0);
        var dz = MathF.Max(MathF.Max(_g.Z0 - p.Y, p.Y - _g.Z1), 0);
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// Lowest terrain height within one <see cref="EdgeStep"/> of the point nearest <paramref name="p"/> on the
    /// terrain, so linear interpolation between neighbouring far vertices never rises above the terrain.
    /// </summary>
    private float EdgeMin(Vector2 p)
    {
        var cx = Math.Clamp(p.X, _g.X0, _g.X1);
        var cz = Math.Clamp(p.Y, _g.Z0, _g.Z1);
        var lo = float.MaxValue;
        var step = _g.Cell;
        for (var dz = -EdgeStep; dz <= EdgeStep + 0.01f; dz += step)
            for (var dx = -EdgeStep; dx <= EdgeStep + 0.01f; dx += step)
                lo = MathF.Min(lo, _g.Height(cx + dx, cz + dz));
        return lo;
    }

    /// <summary>Grid lines: the terrain's span every <see cref="EdgeStep"/> (both ends exact), then widening steps out to the reach.</summary>
    private static List<float> Lines(float lo, float hi, float centre)
    {
        var inner = new List<float>();
        var n = Math.Max(1, (int)MathF.Ceiling((hi - lo) / EdgeStep));
        for (var k = 0; k <= n; k++)
            inner.Add(lo + ((hi - lo) * k / n));
        var outLo = new List<float>();
        var outHi = new List<float>();
        var step = EdgeStep;
        for (float a = lo, b = hi; ;)
        {
            step = MathF.Min(step * 1.35f, 600);
            a -= step;
            b += step;
            var done = a <= centre - Reach && b >= centre + Reach;
            outLo.Add(MathF.Max(a, centre - Reach));
            outHi.Add(MathF.Min(b, centre + Reach));
            if (done || (a <= centre - Reach && b >= centre + Reach))
                break;
        }
        outLo.Reverse();
        return [.. outLo.Distinct(), .. inner, .. outHi.Distinct()];
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - (2 * t));
    }
}
