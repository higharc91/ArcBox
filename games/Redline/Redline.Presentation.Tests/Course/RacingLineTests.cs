using System.Numerics;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Course;

public class RacingLineTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static readonly Lazy<TrackMeshes> Track = new(() => TrackMeshBuilder.Build(Cota));
    private static RacingLine Line => Track.Value.Course.RacingLine;
    private static int M => Samples.Count;

    private static int Index(double s) => (((int)Math.Round(s / Samples[1].S)) % M + M) % M;

    /// <summary>Review focus 4: the line, and both tyre tracks around it, stay on the asphalt with a margin.</summary>
    [Fact]
    public void Racing_line_stays_inside_the_asphalt_with_a_margin()
    {
        for (var i = 0; i < M; i++)
        {
            var half = TrackGeometry.Width(Samples[i].S) / 2;
            Assert.True(Math.Abs(Line.Lateral[i]) <= half - RacingLine.EdgeMargin + 1e-9,
                $"line at s={Samples[i].S:F0} is {Line.Lateral[i]:F2} m out on a {half:F1} m half-width");
        }

        // And on the real asphalt mesh, where a hairpin's inner edge is pinched: every point 1.2 m either side of the line
        // (the outer tyre edges) lies on an asphalt triangle.
        var asphalt = new TriangleBuckets(Track.Value.Asphalt);
        for (var i = 0; i < M; i += 2)
            foreach (var offset in new[] { -1.2, 0, 1.2 })
            {
                var p = TrackMeshBuilder.TrackPoint(Samples, Samples[i].S, Line.Lateral[i] + offset);
                Assert.True(asphalt.Contains(new Vector2(p.X, p.Z)), $"line point at s={Samples[i].S:F0} offset {offset} is off the asphalt");
            }
    }

    [Fact]
    public void Line_takes_the_inside_at_each_apex_and_is_smooth()
    {
        foreach (var span in Track.Value.Course.Corners)
        {
            var i = Index(span.Apex);
            var bound = TrackGeometry.Width(Samples[i].S) / 2 - RacingLine.EdgeMargin;
            Assert.True(Line.Lateral[i] * span.Corner.ApexSide > 0.55 * bound,
                $"T{span.Corner.Turn}: line at {Line.Lateral[i]:F2} m is not on the inside (bound {bound:F2})");
        }
        // Turn 1, a left hairpin after a long straight: in from the right (outside), out to the right.
        var t1 = Track.Value.Course.Corners[0];
        Assert.True(Line.Lateral[Index(t1.Entry - RacingLine.TurnInLead(t1))] < -2);
        Assert.True(Line.Lateral[Index(t1.Exit + RacingLine.TrackOutTrail(t1))] < -2);

        for (var i = 0; i < M; i++)
        {
            // (Up to a metre per 2 m sample: the hairpins swing the line 20 m across in about 50 m of centre line.)
            Assert.True(Math.Abs(Line.Lateral[(i + 1) % M] - Line.Lateral[i]) < 1.0, $"line jumps at s={Samples[i].S:F0}");
            var bend = Line.Lateral[(i + 2) % M] - 2 * Line.Lateral[(i + 1) % M] + Line.Lateral[i];
            Assert.True(Math.Abs(bend) < 0.2, $"line kinks at s={Samples[i].S:F0} ({bend:F3})");
        }
    }

    [Fact]
    public void Skid_marks_before_the_heavy_braking_corners_and_marbles_on_the_outside()
    {
        foreach (var turn in new[] { 1, 11, 12, 15 })
        {
            var span = Track.Value.Course.Corners[turn - 1];
            var peak = Enumerable.Range(0, 100).Select(k => Line.Skid[Index(span.Entry - k)]).Max();
            Assert.True(peak > 0.6, $"T{turn} has no braking-zone skid marks");
            // Marbles past the apex lie on the outside of the line.
            var exit = Enumerable.Range(0, 40).Select(k => Line.Marbles[Index(span.Apex + k)]).MaxBy(Math.Abs);
            Assert.Equal(span.Corner.OutsideSide, Math.Sign(exit));
        }
        // None on the middle of the back straight, and rubber is a weight.
        Assert.Equal(0, Line.Skid[Index(2800)]);
        Assert.Equal(0, Line.Marbles[Index(2800)]);
        Assert.All(Line.Rubber, r => Assert.InRange(r, 0.4, 1));
        Assert.All(Line.Skid, r => Assert.InRange(r, 0, 1));
    }

    [Fact]
    public void Asphalt_carries_the_line_in_its_custom_data()
    {
        var asphalt = Track.Value.Asphalt;
        // Row k (both vertices) carries sample k's line, rubber, skid and marbles.
        foreach (var k in new[] { 0, 170, 1000, 2000 })
            foreach (var v in new[] { 2 * k, 2 * k + 1 })
                Assert.Equal(new Vector4((float)Line.Lateral[k], (float)Line.Rubber[k], (float)Line.Skid[k], (float)Line.Marbles[k]), asphalt.Custom[v]);
    }

    /// <summary>Buckets mesh triangles on an XZ grid for point-in-mesh tests.</summary>
    private sealed class TriangleBuckets
    {
        private const float Bucket = 8;
        private readonly MeshData _m;
        private readonly Dictionary<(int, int), List<int>> _buckets = new();

        public TriangleBuckets(MeshData m)
        {
            _m = m;
            for (var t = 0; t < m.Indices.Count; t += 3)
            {
                var (a, b, c) = (m.Positions[m.Indices[t]], m.Positions[m.Indices[t + 1]], m.Positions[m.Indices[t + 2]]);
                var x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) / Bucket);
                var x1 = (int)MathF.Floor(MathF.Max(a.X, MathF.Max(b.X, c.X)) / Bucket);
                var z0 = (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) / Bucket);
                var z1 = (int)MathF.Floor(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) / Bucket);
                for (var x = x0; x <= x1; x++)
                    for (var z = z0; z <= z1; z++)
                    {
                        if (!_buckets.TryGetValue((x, z), out var list))
                            _buckets[(x, z)] = list = new List<int>();
                        list.Add(t);
                    }
            }
        }

        public bool Contains(Vector2 p)
        {
            if (!_buckets.TryGetValue(((int)MathF.Floor(p.X / Bucket), (int)MathF.Floor(p.Y / Bucket)), out var list))
                return false;
            foreach (var t in list)
            {
                var (a, b, c) = (_m.Positions[_m.Indices[t]], _m.Positions[_m.Indices[t + 1]], _m.Positions[_m.Indices[t + 2]]);
                if (MathF.Abs(Side(new Vector2(c.X, c.Z), a, b)) < 1e-6f) continue; // pinched to nothing
                var d1 = Side(p, a, b);
                var d2 = Side(p, b, c);
                var d3 = Side(p, c, a);
                var neg = d1 < -1e-4f || d2 < -1e-4f || d3 < -1e-4f;
                var pos = d1 > 1e-4f || d2 > 1e-4f || d3 > 1e-4f;
                if (!(neg && pos)) return true;
            }
            return false;
        }

        private static float Side(Vector2 p, Vector3 a, Vector3 b) => (p.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (p.Y - b.Z);
    }
}
