using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// A heightmap mesh around the circuit. Near the track it follows the track elevation 0.15 m below the asphalt; from 30 m to
/// 250 m away (smoothstep on the distance to the centre line) it eases into gentle rolling ground.
/// </summary>
public static class TerrainBuilder
{
    public const double BelowAsphalt = 0.15;
    public const double BlendStart = 30;
    public const double BlendEnd = 250;

    private const int RegionalStride = 8;
    private const double RegionalSigma = 300;
    private const double RegionalLattice = 64;
    private const double CoarseCell = 50;

    private static readonly Vector4 Mown = MeshOps.Hex("#6d8b3a");
    private static readonly Vector4 Field = MeshOps.Hex("#8c8a50");

    /// <summary>
    /// Builds a regular grid with <paramref name="cell"/> metre spacing covering the samples' bounding box grown by
    /// <paramref name="extent"/> metres on every side (COTA: about 3.6 × 2.8 km, 455 × 357 vertices at the defaults).
    /// Vertices are row-major from the north-west corner (x east fastest, then z south). UV = (x, z) in metres; vertex
    /// colours fade from mown green near the track to a drier field.
    /// </summary>
    public static MeshData Build(CircuitData c, IReadOnlyList<TrackSample> samples, double extent = 900, double cell = 8)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(extent);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cell, 0);

        var minX = samples.Min(t => t.Position.X) - extent;
        var maxX = samples.Max(t => t.Position.X) + extent;
        var minZ = samples.Min(t => t.Position.Y) - extent;
        var maxZ = samples.Max(t => t.Position.Y) + extent;
        var cols = (int)Math.Ceiling((maxX - minX) / cell) + 1;
        var rows = (int)Math.Ceiling((maxZ - minZ) / cell) + 1;
        // Centre the (slightly larger) grid on the box.
        var x0 = (minX + maxX - (cols - 1) * cell) / 2;
        var z0 = (minZ + maxZ - (rows - 1) * cell) / 2;

        var field = new HeightField(samples, x0, z0, x0 + (cols - 1) * cell, z0 + (rows - 1) * cell);
        var heights = new double[cols * rows];
        var dists = new double[cols * rows];
        for (var iz = 0; iz < rows; iz++)
            for (var ix = 0; ix < cols; ix++)
                (heights[iz * cols + ix], dists[iz * cols + ix]) = field.Height(x0 + ix * cell, z0 + iz * cell);
        Relax(heights, dists, cols, rows, cell);

        var m = new MeshData();
        for (var iz = 0; iz < rows; iz++)
            for (var ix = 0; ix < cols; ix++)
            {
                var x = x0 + ix * cell;
                var z = z0 + iz * cell;
                var (h, d) = (heights[iz * cols + ix], dists[iz * cols + ix]);
                var shade = (float)(1 + 0.04 * Math.Sin(x * 0.013 + 0.7) * Math.Sin(z * 0.017 - 1.1));
                var colour = Vector4.Clamp(Vector4.Lerp(Mown, Field, (float)Smooth(d / 200)) * shade, Vector4.Zero, Vector4.One);
                colour.W = 1;
                m.AddVertex(new Vector3((float)x, (float)h, (float)z), new Vector2((float)x, (float)z), colour);
            }

        // x runs east (right seen from above) and z south (down), so TL → TR → BR → BL is clockwise from above.
        for (var iz = 0; iz < rows - 1; iz++)
            for (var ix = 0; ix < cols - 1; ix++)
            {
                var tl = iz * cols + ix;
                m.AddQuad(tl, tl + 1, tl + cols + 1, tl + cols);
            }

        m.ComputeNormals();
        return m;
    }

    /// <summary>Relaxation sweeps over the blend zone (see <see cref="Relax"/>), and how far they reach.</summary>
    public const int RelaxSweeps = 60;
    public const double RelaxFrom = BlendStart + 2, RelaxTo = BlendEnd - 30;

    /// <summary>
    /// Between parts of the lap at different heights (the hill up to Turn 1 beside the low stadium section) the blend
    /// zone held each part's height for 30 m and then dropped in a short, steep, shadowed bank. Relaxing it towards the
    /// average of its neighbours (a harmonic fill between the track-following verge and the far field, each held fixed)
    /// spreads that drop evenly over the whole gap. Vertices within <see cref="RelaxFrom"/> of the centre line and
    /// beyond <see cref="RelaxTo"/> do not move; the strength eases in and out between.
    /// </summary>
    private static void Relax(double[] h, double[] d, int cols, int rows, double cell)
    {
        var weight = new double[h.Length];
        var active = new List<int>();
        for (var i = 0; i < h.Length; i++)
        {
            weight[i] = Smooth((d[i] - RelaxFrom) / (4 * cell)) * (1 - Smooth((d[i] - (RelaxTo - 40)) / 40));
            var ix = i % cols;
            var iz = i / cols;
            if (weight[i] > 0 && ix > 0 && iz > 0 && ix < cols - 1 && iz < rows - 1)
                active.Add(i);
        }
        var next = (double[])h.Clone();
        for (var sweep = 0; sweep < RelaxSweeps; sweep++)
        {
            foreach (var i in active)
            {
                var avg = (h[i - 1] + h[i + 1] + h[i - cols] + h[i + cols]) / 4;
                next[i] = h[i] + (avg - h[i]) * weight[i];
            }
            foreach (var i in active)
                h[i] = next[i];
        }
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Gentle rolling noise in metres, about ±4 m.</summary>
    private static double Noise(double x, double z) =>
        2.4 * Math.Sin(x * 0.0041 + 1.3) * Math.Cos(z * 0.0049 - 0.4)
        + 1.1 * Math.Sin((x + z) * 0.0107 + 2.1)
        + 0.5 * Math.Sin(x * 0.031 - z * 0.027);

    private sealed class HeightField
    {
        private readonly IReadOnlyList<TrackSample> _q;
        private readonly SampleGrid _grid;
        // Coarse cells that can hold a point within BlendEnd of the centre line; everything else is pure far field.
        private readonly HashSet<(int, int)> _near = new();
        private readonly double _x0, _z0;
        private readonly int _lw, _lh;
        private readonly double[] _regional;

        public HeightField(IReadOnlyList<TrackSample> q, double x0, double z0, double x1, double z1)
        {
            _q = q;
            _grid = new SampleGrid(q);

            var reach = (int)Math.Ceiling(BlendEnd / CoarseCell) + 1;
            foreach (var t in q)
            {
                var cx = (int)Math.Floor(t.Position.X / CoarseCell);
                var cz = (int)Math.Floor(t.Position.Y / CoarseCell);
                for (var dx = -reach; dx <= reach; dx++)
                    for (var dz = -reach; dz <= reach; dz++)
                        _near.Add((cx + dx, cz + dz));
            }

            // The regional height is smooth on a 300 m scale, so evaluate it on a lattice and interpolate.
            (_x0, _z0) = (x0, z0);
            _lw = (int)Math.Ceiling((x1 - x0) / RegionalLattice) + 2;
            _lh = (int)Math.Ceiling((z1 - z0) / RegionalLattice) + 2;
            _regional = new double[_lw * _lh];
            var mean = q.Average(t => t.Height);
            for (var j = 0; j < _lh; j++)
                for (var i = 0; i < _lw; i++)
                    _regional[j * _lw + i] = Regional(x0 + i * RegionalLattice, z0 + j * RegionalLattice, mean);
        }

        /// <summary>Terrain height at (x, z), and the distance to the centre line (capped at <see cref="BlendEnd"/>).</summary>
        public (double Height, double Distance) Height(double x, double z)
        {
            var far = RegionalAt(x, z) + 1.5 + Noise(x, z);
            if (!_near.Contains(((int)Math.Floor(x / CoarseCell), (int)Math.Floor(z / CoarseCell))))
                return (far, BlendEnd);
            var (i, _) = _grid.Nearest(x, z, BlendEnd);
            if (i < 0)
                return (far, BlendEnd);

            // Project onto the two centre-line segments either side of the nearest sample, so the height is exact (and
            // continuous along the lap) under the asphalt.
            var n = _q.Count;
            var p = new Vector2((float)x, (float)z);
            var (d, h) = Project(_q[(i - 1 + n) % n], _q[i], p);
            var (d2, h2) = Project(_q[i], _q[(i + 1) % n], p);
            if (d2 < d) (d, h) = (d2, h2);
            if (d >= BlendEnd)
                return (far, BlendEnd);

            // Beyond the verge, ease the nearest foot's height into a soft minimum over every part of the lap in reach, so
            // there is no crease along the line halfway between two parts of the lap at different heights.
            if (d > BlendStart)
                h += (SoftHeight(x, z, d, h) - h) * Smooth((d - BlendStart) / (2 * BlendStart));

            var near = h - BelowAsphalt;
            var blend = Smooth((d - BlendStart) / (BlendEnd - BlendStart));
            return (near + (far - near) * blend, d);
        }

        /// <summary>Track height averaged with weights exp(-(dᵢ - d) / τ), τ = d / 4, over every 4th sample in reach.</summary>
        private double SoftHeight(double x, double z, double d, double fallback)
        {
            var tau = d / 4;
            var reach2 = (d + 4 * tau) * (d + 4 * tau);
            double wSum = 0, hSum = 0;
            for (var i = 0; i < _q.Count; i += 4)
            {
                var d2 = _grid.Dist2(i, x, z);
                if (d2 > reach2) continue;
                var w = Math.Exp(-(Math.Sqrt(d2) - d) / tau);
                wSum += w;
                hSum += w * _q[i].Height;
            }
            return wSum > 0 ? hSum / wSum : fallback;
        }

        private static (double Distance, double Height) Project(TrackSample a, TrackSample b, Vector2 p)
        {
            var ab = b.Position - a.Position;
            var len2 = ab.LengthSquared();
            var t = len2 > 0 ? Math.Clamp(Vector2.Dot(p - a.Position, ab) / len2, 0, 1) : 0;
            var foot = a.Position + ab * t;
            return (Vector2.Distance(p, foot), a.Height + (b.Height - a.Height) * t);
        }

        private double RegionalAt(double x, double z)
        {
            var fx = Math.Clamp((x - _x0) / RegionalLattice, 0, _lw - 1.001);
            var fz = Math.Clamp((z - _z0) / RegionalLattice, 0, _lh - 1.001);
            int i = (int)fx, j = (int)fz;
            double tx = fx - i, tz = fz - j;
            var top = _regional[j * _lw + i] * (1 - tx) + _regional[j * _lw + i + 1] * tx;
            var bottom = _regional[(j + 1) * _lw + i] * (1 - tx) + _regional[(j + 1) * _lw + i + 1] * tx;
            return top * (1 - tz) + bottom * tz;
        }

        /// <summary>Gaussian-weighted average of the track heights, so the far field rises and falls with the circuit.</summary>
        private double Regional(double x, double z, double mean)
        {
            double wSum = 1e-3, hSum = 1e-3 * mean;
            for (var i = 0; i < _q.Count; i += RegionalStride)
            {
                var w = Math.Exp(-_grid.Dist2(i, x, z) / (2 * RegionalSigma * RegionalSigma));
                wSum += w;
                hSum += w * _q[i].Height;
            }
            return hSum / wSum;
        }
    }
}
