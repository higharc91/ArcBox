using System.Numerics;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Meshes;

public class TrackMeshBuilderTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static readonly Lazy<TrackMeshes> Track = new(() => TrackMeshBuilder.Build(Cota));
    private static readonly Lazy<MeshData> Terrain = new(() => TerrainBuilder.Build(Cota, Samples));

    private static int M => Samples.Count;

    private static IEnumerable<(string Name, MeshData Mesh)> AllMeshes()
    {
        var t = Track.Value;
        yield return ("Asphalt", t.Asphalt);
        yield return ("Lines", t.Lines);
        yield return ("Curbs", t.Curbs);
        yield return ("Runoff", t.Runoff);
        yield return ("Walls", t.Walls);
        yield return ("Fence", t.Fence);
        yield return ("Grass", t.Grass);
        yield return ("Gravel", t.Gravel);
        yield return ("Terrain", Terrain.Value);
    }

    private static int NearestIndex(double s) =>
        Enumerable.Range(0, M).MinBy(i => Math.Abs(Samples[i].S - s));

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private const float Bucket = 20;
    private static readonly Lazy<ILookup<(int, int), int>> SampleBuckets = new(() =>
        Enumerable.Range(0, M).ToLookup(i => BucketOf(Samples[i].Position)));

    private static (int, int) BucketOf(Vector2 p) => ((int)MathF.Floor(p.X / Bucket), (int)MathF.Floor(p.Y / Bucket));

    /// <summary>Nearest centre-line sample, searching buckets in growing square rings; (-1, ∞) beyond maxDistance.</summary>
    private static (int Index, float Distance) NearestSample(Vector3 v, float maxDistance = 5000)
    {
        var maxRing = (int)MathF.Ceiling(maxDistance / Bucket) + 1;
        var p = Flat(v);
        var (cx, cz) = BucketOf(p);
        var best = float.MaxValue;
        var bi = -1;
        for (var r = 0; ; r++)
        {
            for (var dx = -r; dx <= r; dx++)
                for (var dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    foreach (var i in SampleBuckets.Value[(cx + dx, cz + dz)])
                    {
                        var d = Vector2.DistanceSquared(p, Samples[i].Position);
                        if (d < best) { best = d; bi = i; }
                    }
                }
            if (bi >= 0 && best <= (r * Bucket) * (r * Bucket)) break;
            if (r >= maxRing) break;
        }
        return bi >= 0 && best <= maxDistance * maxDistance ? (bi, MathF.Sqrt(best)) : (-1, float.PositiveInfinity);
    }

    /// <summary>Distance to, and asphalt height at, the closest point of the centre line segments either side of sample i.</summary>
    private static (double Distance, double Height) CentreLineFoot(int i, Vector2 p)
    {
        var best = (Distance: double.MaxValue, Height: 0.0);
        foreach (var (a, b) in new[] { (Samples[(i - 1 + M) % M], Samples[i]), (Samples[i], Samples[(i + 1) % M]) })
        {
            var ab = b.Position - a.Position;
            var t = Math.Clamp(Vector2.Dot(p - a.Position, ab) / ab.LengthSquared(), 0, 1);
            var d = Vector2.Distance(p, a.Position + ab * t);
            if (d < best.Distance) best = (d, a.Height + (b.Height - a.Height) * t);
        }
        return best;
    }

    /// <summary>Distance to, and height at, the closest point of centre-line segment j → j + 1.</summary>
    private static (double Distance, double Height) SegmentFoot(int j, Vector2 p)
    {
        var a = Samples[j];
        var b = Samples[(j + 1) % M];
        var ab = b.Position - a.Position;
        var t = Math.Clamp(Vector2.Dot(p - a.Position, ab) / ab.LengthSquared(), 0, 1);
        return (Vector2.Distance(p, a.Position + ab * t), a.Height + (b.Height - a.Height) * t);
    }

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, int Ia, int Ib, int Ic)> Triangles(MeshData m)
    {
        for (var i = 0; i < m.Indices.Count; i += 3)
        {
            int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
            yield return (m.Positions[a], m.Positions[b], m.Positions[c], a, b, c);
        }
    }

    [Fact]
    public void Asphalt_is_closed_ribbon_with_expected_triangle_count()
    {
        var asphalt = Track.Value.Asphalt;

        Assert.Equal(2 * M, asphalt.Indices.Count / 3);
        // M + 1 rows of (left, right); the last row repeats the first so U can run on past the seam.
        Assert.Equal(2 * (M + 1), asphalt.Positions.Count);
        Assert.True(Vector3.Distance(asphalt.Positions[0], asphalt.Positions[2 * M]) < 1e-3f);
        Assert.True(Vector3.Distance(asphalt.Positions[1], asphalt.Positions[2 * M + 1]) < 1e-3f);
        Assert.Equal(Enumerable.Range(0, asphalt.Positions.Count), asphalt.Indices.Distinct().Order());

        // Every row spans its sample's cross-section at the sample's height, except the few rows where the inside edge of a
        // hairpin is pinched to stop it folding (Turn 1's true radius is about 13 m against a 12 m half-width).
        var pinched = 0;
        for (var k = 0; k < M; k++)
        {
            var left = asphalt.Positions[2 * k];
            var right = asphalt.Positions[2 * k + 1];
            var q = Samples[k];
            var half = TrackGeometry.Width(q.S) / 2;
            var exact = Vector2.Distance(Flat(left), q.Position + q.Left * (float)half) < 1e-3f
                && Vector2.Distance(Flat(right), q.Position - q.Left * (float)half) < 1e-3f;
            if (!exact)
            {
                pinched++;
                Assert.True(TrackGeometry.IsCorner(q), $"row at s={q.S:F0} is pinched outside a corner");
                continue;
            }
            Assert.InRange(left.Y, q.Height - 1e-3, q.Height + 1e-3);
            Assert.InRange(right.Y, q.Height - 1e-3, q.Height + 1e-3);
        }
        Assert.InRange(pinched, 0, 20);
    }

    [Fact]
    public void All_normals_are_unit_and_no_NaN()
    {
        foreach (var (name, m) in AllMeshes())
        {
            Assert.True(m.Positions.Count > 0, name);
            Assert.Equal(m.Positions.Count, m.Normals.Count);
            Assert.Equal(m.Positions.Count, m.Uvs.Count);
            Assert.Equal(m.Positions.Count, m.Colors.Count);
            Assert.Equal(0, m.Indices.Count % 3);
            Assert.All(m.Indices, i => Assert.InRange(i, 0, m.Positions.Count - 1));
            Assert.All(m.Positions, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), name));
            Assert.All(m.Uvs, uv => Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y), name));
            Assert.All(m.Colors, c => Assert.True(c.X is >= 0 and <= 1 && c.Y is >= 0 and <= 1 && c.Z is >= 0 and <= 1 && c.W == 1, name));
            Assert.All(m.Normals, n => Assert.InRange(n.Length(), 0.999f, 1.001f));
            Assert.True(m.Custom.Count == 0 || m.Custom.Count == m.Positions.Count, name);
        }
        Assert.Equal(Track.Value.Asphalt.Positions.Count, Track.Value.Asphalt.Custom.Count);
        Assert.Equal(Track.Value.Curbs.Positions.Count, Track.Value.Curbs.Custom.Count);
    }

    [Fact]
    public void Front_faces_are_clockwise_and_agree_with_normals()
    {
        // Godot 4 convention: clockwise seen from the front, so Cross(b - a, c - a) points away from the front.
        // (Zero-area triangles are where a hairpin's inside edge is pinched to stop it folding.)
        foreach (var (a, b, c, _, _, _) in Triangles(Track.Value.Asphalt))
        {
            var away = Vector3.Cross(b - a, c - a);
            Assert.True(away.Y < 0 || away.Length() < 1e-6f, "asphalt triangle must be clockwise seen from above");
        }
        Assert.All(Track.Value.Asphalt.Normals, n => Assert.True(n.Y > 0.9f));

        foreach (var (name, m) in AllMeshes())
            foreach (var (a, b, c, ia, ib, ic) in Triangles(m))
            {
                var away = Vector3.Cross(b - a, c - a);
                if (away.Length() < 1e-6f) continue;
                var normal = m.Normals[ia] + m.Normals[ib] + m.Normals[ic];
                Assert.True(Vector3.Dot(away, normal) < 0, $"{name} triangle {ia},{ib},{ic} winding disagrees with its normals");
            }
    }

    [Fact]
    public void Asphalt_width_matches_profile()
    {
        var asphalt = Track.Value.Asphalt;
        foreach (var (s, width) in new[] { (0.0, 15.0), (300.0, 24.0) })
        {
            var k = NearestIndex(s);
            Assert.Equal(width, TrackGeometry.Width(Samples[k].S), 1);
            Assert.InRange(Vector3.Distance(asphalt.Positions[2 * k], asphalt.Positions[2 * k + 1]), width - 0.1, width + 0.1);
        }
    }

    [Fact]
    public void Walls_skip_inside_of_turn_one_hairpin()
    {
        var t = Track.Value;

        foreach (var (name, m) in new[] { ("Walls", t.Walls), ("Fence", t.Fence) })
        {
            // Never on the asphalt or curbs of any part of the lap (a folded offset would cut back across it).
            foreach (var v in m.Positions)
            {
                var (i, d) = NearestSample(v);
                Assert.True(d > TrackGeometry.Width(Samples[i].S) / 2 + 3, $"{name} vertex {v} is {d:F1} m from the centre line at s={Samples[i].S:F0}");
            }

            // The visible side always faces the track; a fold would flip it.
            foreach (var (a, b, c, _, _, _) in Triangles(m))
            {
                var front = -Vector3.Cross(b - a, c - a);
                if (front.Length() < 1e-6f) continue;
                var centroid = (a + b + c) / 3;
                var (i, _) = NearestSample(centroid);
                var toTrack = Samples[i].Position - Flat(centroid);
                Assert.True(Vector2.Dot(Flat(front), toTrack) > 0, $"{name} triangle near s={Samples[i].S:F0} faces away from the track");
            }
        }

        // Turn 1 is a left hairpin: through the apex its inner (left) side has neither wall nor fence, while the outer
        // (right) side keeps both.
        var apex = NearestIndex(340);
        foreach (var m in new[] { t.Walls, t.Fence })
        {
            var atApex = m.Positions.Select(v => (v, NearestSample(v).Index)).Where(p => Math.Abs(p.Index - apex) <= 8).ToArray();
            int Side((Vector3 V, int Index) p) => Math.Sign(Vector2.Dot(Flat(p.V) - Samples[p.Index].Position, Samples[p.Index].Left));
            Assert.DoesNotContain(atApex, p => Side(p) > 0);
            Assert.Contains(atApex, p => Side(p) < 0);
        }
    }

    /// <summary>The lap position (metres, unwrapped near <paramref name="near"/>) and lateral of a point, from its nearest sample.</summary>
    private static (double S, double Lateral, int Index) Locate(Vector3 v)
    {
        var (i, _) = NearestSample(v);
        var q = Samples[i];
        var rel = Flat(v) - q.Position;
        return (q.S + Vector2.Dot(rel, q.Tangent), Vector2.Dot(rel, q.Left), i);
    }

    private static bool Covers(double from, double to, double s, double slack)
    {
        var lap = Samples[1].S * M;
        for (var k = -1; k <= 1; k++)
            if (s + k * lap >= from - slack && s + k * lap <= to + slack) return true;
        return false;
    }

    [Fact]
    public void Kerbs_follow_the_corner_runs_with_metre_stripes()
    {
        var curbs = Track.Value.Curbs;
        var course = Track.Value.Course;

        // Every kerb vertex is beside the asphalt within a kerb or sausage run on its side.
        foreach (var v in curbs.Positions)
        {
            var (s, lat, i) = Locate(v);
            var side = Math.Sign(lat);
            Assert.True(course.Kerbs.Any(r => r.Side == side && Covers(r.From, r.To, s, 3))
                || course.Sausages.Any(r => r.Side == side && Covers(r.From, r.To, s, 3)),
                $"kerb vertex at s={s:F0}, lateral {lat:F1} belongs to no run");
        }

        var straight = Samples[NearestIndex(5300)];
        Assert.DoesNotContain(curbs.Positions, v => Vector2.Distance(Flat(v), straight.Position) < 30);

        // Red and white alternate every metre along the kerb (UV.x is metres along it); sausages are yellow.
        var red = CourseMeshes.KerbRed;
        var white = CourseMeshes.KerbWhite;
        for (var t = 0; t < curbs.Indices.Count; t += 3)
        {
            int a = curbs.Indices[t], b = curbs.Indices[t + 1], c = curbs.Indices[t + 2];
            var colour = curbs.Colors[a];
            if (colour == CourseMeshes.SausageYellow)
            {
                Assert.Equal(CourseMeshes.SausageCode, curbs.Custom[a].X);
                continue;
            }
            if (colour == CourseMeshes.KerbSeam)
            {
                // The sealed joint lies just inside the asphalt edge.
                Assert.All(new[] { a, b, c }, k => Assert.InRange(curbs.Uvs[k].Y, -Kerbs.SeamWidth - 1e-4, 1e-4));
                continue;
            }
            var us = new[] { curbs.Uvs[a].X, curbs.Uvs[b].X, curbs.Uvs[c].X };
            Assert.True(us.Max() - us.Min() <= Kerbs.StripeLength + 1e-3, "a stripe facet is longer than a stripe");
            var stripe = (int)Math.Floor((us.Max() + us.Min()) / 2 / Kerbs.StripeLength) % 2;
            Assert.Equal(stripe == 0 ? red : white, colour);
        }
        Assert.Contains(curbs.Colors, c => c == CourseMeshes.SausageYellow);
        Assert.Contains(curbs.Custom, c => c.X == CourseMeshes.SawtoothCode);
        Assert.Contains(curbs.Custom, c => c.X == CourseMeshes.FlatCode);
    }

    [Fact]
    public void Runoff_and_gravel_lie_in_the_profile_on_corner_outsides()
    {
        var t = Track.Value;
        var profile = t.Course.Runoff;
        foreach (var (name, mesh) in new[] { ("Runoff", t.Runoff), ("Gravel", t.Gravel) })
            foreach (var v in mesh.Positions)
            {
                var (s, lat, i) = Locate(v);
                var side = Math.Sign(lat);
                var half = TrackGeometry.Width(Samples[i].S) / 2;
                Assert.True(profile.Areas.Any(a => a.Side == side && Covers(a.From, a.To, s, RunoffProfile.Taper + 3)),
                    $"{name} vertex at s={s:F0} lateral {lat:F1} is outside every run-off area");
                Assert.InRange(Math.Abs(lat), half - 0.6, half + Kerbs.Width + RunoffProfile.FullWidth(RunoffKind.TarmacStriped) + 0.6);
            }

        // Turn 1 turns left: its painted run-off is on the right; the esses have gravel.
        var t1 = Samples[NearestIndex(340)];
        Assert.Contains(t.Runoff.Positions, v => Locate(v) is var p && Math.Abs(p.S - t1.S) < 10 && p.Lateral < 0
            && t.Runoff.Colors[t.Runoff.Positions.IndexOf(v)] == CourseMeshes.RunoffBlue);
        Assert.Contains(t.Gravel.Positions, v => Locate(v).S is > 820 and < 1100);
        Assert.Contains(profile.Areas, a => a.Turn == 1 && a.Side < 0 && a.Kind == RunoffKind.TarmacStriped);
    }

    [Fact]
    public void Walls_stay_behind_the_run_off()
    {
        var t = Track.Value;
        foreach (var (name, mesh) in new[] { ("Walls", t.Walls), ("Fence", t.Fence) })
            foreach (var v in mesh.Positions)
            {
                var (_, lat, i) = Locate(v);
                Assert.True(Math.Abs(lat) >= t.Course.Runoff.Outer(i, Math.Sign(lat)) - 0.5,
                    $"{name} vertex at s={Samples[i].S:F0} is {Math.Abs(lat):F1} m out, inside the run-off ({t.Course.Runoff.Outer(i, Math.Sign(lat)):F1} m)");
            }
    }

    /// <summary>Review focus 1: nothing floats or sinks on the slopes.</summary>
    [Fact]
    public void Markings_kerbs_runoff_and_gravel_sit_on_the_track_surface()
    {
        var t = Track.Value;
        // (layer, lowest and highest height above the surface at the vertex's foot on the centre line)
        foreach (var (name, mesh, lo, hi) in new[]
                 {
                     ("Lines", t.Lines, 0.012, 0.03),
                     ("Curbs", t.Curbs, -0.035, 0.16),
                     ("Runoff", t.Runoff, 0.0, 0.015),
                     ("Gravel", t.Gravel, -0.012, 0.0),
                 })
        {
            var checkedSlope = 0;
            foreach (var v in mesh.Positions)
            {
                var (i, _) = NearestSample(v);
                // The surface under a point is the height at its foot on the centre line. Near the centre of a hairpin many
                // segments are almost equally close, so every segment foot within 0.15 m of the nearest is a fair foot.
                var feet = Enumerable.Range(-40, 81)
                    .Select(k => SegmentFoot(((i + k) % M + M) % M, Flat(v)))
                    .ToArray();
                var d = feet.Min(f => f.Distance);
                var fair = feet.Where(f => f.Distance <= d + 0.15).Select(f => f.Height).ToArray();
                var (foot, low, high) = (fair[0], fair.Min(), fair.Max());
                const double Slack = 0.02;
                // Fine if some fair foot puts it within [lo, hi] above the surface.
                Assert.True(v.Y - low >= lo - Slack && v.Y - high <= hi + Slack,
                    $"{name} vertex at s={Samples[i].S:F0} is {v.Y - foot:F3} m above the track surface ({v}; foot range {low:F3}..{high:F3}, d {d:F2})");
                if (Samples[i].S is > 150 and < 330 or > 600 and < 1100) checkedSlope++;
            }
            if (name != "Gravel") Assert.True(checkedSlope > 50, $"{name}: too few vertices on the Turn 1 climb and the esses descent");
        }
    }

    [Fact]
    public void Start_finish_chequer_at_s0()
    {
        var lines = Track.Value.Lines;
        // (Black paint, not the slightly lighter sealant of the repair seams.)
        var black = lines.Positions.Where((_, i) => lines.Colors[i].X < 0.08f).ToArray();

        Assert.NotEmpty(black);
        Assert.All(black, v => Assert.True(Vector2.Distance(Flat(v), Samples[0].Position) < 15.0 / 2 + 1.5));
    }

    [Fact]
    public void Track_point_interpolates_position_height_and_lateral()
    {
        var k = NearestIndex(1000);
        var q = Samples[k];

        var centre = TrackMeshBuilder.TrackPoint(Samples, q.S, 0);
        Assert.True(Vector2.Distance(Flat(centre), q.Position) < 1e-3f);
        Assert.Equal(q.Height, centre.Y, 3);

        var left = TrackMeshBuilder.TrackPoint(Samples, q.S, 5);
        Assert.True(Vector2.Distance(Flat(left), q.Position + q.Left * 5) < 1e-2f);

        // Halfway between two samples, and wrapped by whole laps either way.
        var mid = TrackMeshBuilder.TrackPoint(Samples, (q.S + Samples[k + 1].S) / 2, 0);
        Assert.True(Vector2.Distance(Flat(mid), (q.Position + Samples[k + 1].Position) / 2) < 1e-2f);
        var lap = Samples[1].S * M;
        Assert.True(Vector3.Distance(centre, TrackMeshBuilder.TrackPoint(Samples, q.S + lap, 0)) < 1e-2f);
        Assert.True(Vector3.Distance(centre, TrackMeshBuilder.TrackPoint(Samples, q.S - lap, 0)) < 1e-2f);

        // Past the last sample it closes onto the first.
        var seam = TrackMeshBuilder.TrackPoint(Samples, lap - 1e-6, 0);
        Assert.True(Vector2.Distance(Flat(seam), Samples[0].Position) < 1e-2f);
    }

    [Fact]
    public void Terrain_covers_the_circuit_at_the_requested_resolution()
    {
        // A regular 8 m grid reaching 900 m past the circuit on every side (COTA is 1.83 × 1.04 km, so a 1.8 km square
        // centred on it would not even cover the track).
        var terrain = Terrain.Value;
        var cols = terrain.Positions.Select(p => p.X).Distinct().Count();
        var rows = terrain.Positions.Select(p => p.Z).Distinct().Count();

        Assert.Equal(cols * rows, terrain.Positions.Count);
        Assert.Equal(2 * (cols - 1) * (rows - 1), terrain.Indices.Count / 3);
        Assert.Equal(8, terrain.Positions[1].X - terrain.Positions[0].X, 3);
        Assert.Equal(8, terrain.Positions[cols].Z - terrain.Positions[0].Z, 3);
        Assert.InRange(terrain.Positions.Count, 100_000, 200_000);

        var minX = terrain.Positions.Min(p => p.X);
        var maxX = terrain.Positions.Max(p => p.X);
        var minZ = terrain.Positions.Min(p => p.Z);
        var maxZ = terrain.Positions.Max(p => p.Z);
        Assert.All(Samples, q => Assert.True(q.Position.X >= minX + 900 && q.Position.X <= maxX - 900 && q.Position.Y >= minZ + 900 && q.Position.Y <= maxZ - 900));
    }

    [Fact]
    public void Terrain_sits_below_asphalt_near_track()
    {
        var terrain = Terrain.Value;

        // Vertices inside the track width sit 0.15 m under the asphalt (the height at their foot on the centre line).
        var inside = 0;
        foreach (var v in terrain.Positions)
        {
            var (i, _) = NearestSample(v, 20);
            if (i < 0) continue;
            var (d, asphalt) = CentreLineFoot(i, Flat(v));
            if (d > TrackGeometry.Width(Samples[i].S) / 2) continue;
            inside++;
            Assert.InRange(v.Y, asphalt - 0.15 - 0.01, asphalt - 0.15 + 0.01);
        }
        Assert.True(inside > 1000);

        // And the interpolated surface never pokes through: check every asphalt vertex and the centre line. The margin is
        // under 0.15 m because 8 m terrain triangles chord the elevation curve and Turn 1's pinched inner corner takes its
        // height from the edge rather than the centre line (0.08 m there).
        var index = new TriangleIndex(terrain);
        var points = Track.Value.Asphalt.Positions.Concat(Samples.Select(q => new Vector3(q.Position.X, (float)q.Height, q.Position.Y)));
        foreach (var p in points)
        {
            var h = index.HeightAt(Flat(p));
            Assert.True(h < p.Y - 0.05, $"terrain {h:F2} vs asphalt {p.Y:F2} at {p}");
        }
    }

    [Fact]
    public void Pack_offsets_stay_inside_track_width()
    {
        foreach (var q in Samples)
        {
            var half = TrackGeometry.Width(q.S) / 2;
            for (var size = 1; size <= 22; size++)
                for (var i = 0; i < size; i++)
                    Assert.True(Math.Abs(LapMath.Pack(i, size).Lateral) + 1 < half);
            for (var g = 0; g < 22; g++)
                Assert.True(Math.Abs(LapMath.Grid(g).Lateral) + 1 < half);
        }
    }

    /// <summary>Buckets terrain triangles on an XZ grid to find the surface height under a point.</summary>
    private sealed class TriangleIndex
    {
        private const float Bucket = 8;
        private readonly MeshData _m;
        private readonly Dictionary<(int, int), List<int>> _buckets = new();

        public TriangleIndex(MeshData m)
        {
            _m = m;
            for (var t = 0; t < m.Indices.Count; t += 3)
            {
                var a = m.Positions[m.Indices[t]];
                var b = m.Positions[m.Indices[t + 1]];
                var c = m.Positions[m.Indices[t + 2]];
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

        public double HeightAt(Vector2 p)
        {
            var key = ((int)MathF.Floor(p.X / Bucket), (int)MathF.Floor(p.Y / Bucket));
            foreach (var t in _buckets[key])
            {
                var a = _m.Positions[_m.Indices[t]];
                var b = _m.Positions[_m.Indices[t + 1]];
                var c = _m.Positions[_m.Indices[t + 2]];
                var det = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                var wa = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Z)) / det;
                var wb = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Z)) / det;
                var wc = 1 - wa - wb;
                if (wa >= -1e-4f && wb >= -1e-4f && wc >= -1e-4f)
                    return wa * a.Y + wb * b.Y + wc * c.Y;
            }
            throw new InvalidOperationException($"No terrain under {p}.");
        }
    }
}
