using System.Numerics;
using Redline.Presentation.Course;

namespace Redline.Presentation.Meshes;

/// <summary>
/// Unfolded offset edges of the track (see <see cref="TrackMeshBuilder.Unfold"/>), one closed polyline of M + 1 points per
/// (side, offset beyond the asphalt edge, height above the track), built once and shared by the kerbs and run-off.
/// </summary>
internal sealed class EdgeCache(IReadOnlyList<TrackSample> q, double[] half)
{
    private readonly Dictionary<(int, double, float), Vector3[]> _cache = new();

    public IReadOnlyList<TrackSample> Samples => q;

    /// <summary>The edge <paramref name="offset"/> metres beyond the asphalt edge on <paramref name="side"/> (+1 left).</summary>
    public Vector3[] Edge(int side, double offset, float lift)
    {
        if (!_cache.TryGetValue((side, offset, lift), out var e))
            _cache[(side, offset, lift)] = e = Edge(side, i => half[i] + offset, lift);
        return e;
    }

    /// <summary>The edge at <paramref name="distance"/>(i) metres from the centre line on <paramref name="side"/>, uncached.</summary>
    public Vector3[] Edge(int side, Func<int, double> distance, float lift)
    {
        var n = q.Count;
        var e = new Vector3[n + 1];
        for (var k = 0; k <= n; k++)
            e[k] = TrackMeshBuilder.At(q[k % n], side * distance(k % n), lift);
        TrackMeshBuilder.Unfold(e, q);
        return e;
    }

    /// <summary>Whether row k of an edge from this cache was moved by the unfolding (pinched inside a tight bend).</summary>
    public bool Pinched(Vector3[] e, int side, double offset, int k)
    {
        var n = q.Count;
        var nominal = TrackMeshBuilder.At(q[k % n], side * (half[k % n] + offset), e[k].Y - (float)q[k % n].Height);
        return Vector2.DistanceSquared(new Vector2(e[k].X, e[k].Z), new Vector2(nominal.X, nominal.Z)) > 1e-4f;
    }
}

/// <summary>Builds the course dressing (kerbs, sausage kerbs, run-off, gravel, painted marks) from a <see cref="CourseLayout"/>.</summary>
public static class CourseMeshes
{
    public const float MarkLift = 0.023f, SealantLift = 0.016f, RunoffLift = 0.008f, GravelLift = -0.005f;
    /// <summary>COTA's painted run-off stripes, each this long along the run-off's inner edge.</summary>
    public const double RunoffStripe = 3;
    /// <summary>Kerb heights ease from the verge to full height over this distance at each end of a run.</summary>
    public const double KerbRamp = 1.2, SausageRamp = 0.15;
    /// <summary>Longest piece a painted mark is cut into, so it follows the surface.</summary>
    private const double MarkPiece = 1.5;

    public static readonly Vector4 KerbRed = MeshOps.Hex("#d8001f");
    public static readonly Vector4 KerbWhite = MeshOps.Hex("#f2f2f2");
    public static readonly Vector4 SausageYellow = MeshOps.Hex("#f2c200");
    public static readonly Vector4 RunoffRed = MeshOps.Hex("#a3333c");
    public static readonly Vector4 RunoffWhite = MeshOps.Hex("#d6d6d1");
    public static readonly Vector4 RunoffBlue = MeshOps.Hex("#2e4c80");
    /// <summary>The dark sealed joint along a kerb's base, between it and the asphalt.</summary>
    public static readonly Vector4 KerbSeam = MeshOps.Hex("#1e1e20");
    public static readonly Vector4 Tarmac = MeshOps.Hex("#4a4b50");
    public static readonly Vector4 GravelColour = MeshOps.Hex("#b29e80");
    private static readonly Vector4 White = MeshOps.Hex("#f1f1ee");
    private static readonly Vector4 Black = MeshOps.Hex("#111111");
    private static readonly Vector4 Sealant = MeshOps.Hex("#18181a");

    /// <summary>Kerb kinds as written to <see cref="MeshData.Custom"/>.X for the kerb material.</summary>
    public const float FlatCode = 0, SawtoothCode = 1, SausageCode = 2;

    // ---- Painted marks --------------------------------------------------------------------------------------

    /// <summary>Drapes every mark on the asphalt, cut into pieces of at most <see cref="MarkPiece"/> metres.</summary>
    internal static void Marks(MeshData m, IReadOnlyList<TrackSample> q, IReadOnlyList<Mark> marks)
    {
        foreach (var mark in marks)
        {
            var (a, b, c, d) = (mark.A, mark.B, mark.C, mark.D);
            // Clockwise seen from above is clockwise in (s, lateral) with lateral up: negative signed area.
            var area = Cross(b - a, c - a) + Cross(c - a, d - a);
            if (area > 0) (b, d) = (d, b);
            var nu = Math.Max(1, (int)Math.Ceiling(Math.Max(Vector2.Distance(a, b), Vector2.Distance(d, c)) / MarkPiece));
            var nv = Math.Max(1, (int)Math.Ceiling(Math.Max(Vector2.Distance(a, d), Vector2.Distance(b, c)) / MarkPiece));
            var (colour, lift) = mark.Paint switch
            {
                Paint.Black => (Black, MarkLift),
                Paint.Sealant => (Sealant, SealantLift),
                _ => (White, MarkLift),
            };
            var first = m.Positions.Count;
            for (var j = 0; j <= nv; j++)
                for (var i = 0; i <= nu; i++)
                {
                    float u = (float)i / nu, v = (float)j / nv;
                    var p = Vector2.Lerp(Vector2.Lerp(a, b, u), Vector2.Lerp(d, c, u), v);
                    var world = TrackMeshBuilder.TrackPoint(q, p.X, p.Y);
                    m.AddVertex(world with { Y = world.Y + lift }, p, colour);
                }
            for (var j = 0; j < nv; j++)
                for (var i = 0; i < nu; i++)
                {
                    var v0 = first + j * (nu + 1) + i;
                    m.AddQuad(v0, v0 + 1, v0 + nu + 2, v0 + nu + 1);
                }
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // ---- Kerbs ----------------------------------------------------------------------------------------------

    internal static void Kerbs(MeshData m, IReadOnlyList<TrackSample> q, EdgeCache edges, CourseLayout course)
    {
        foreach (var run in course.Kerbs)
        {
            var profile = Course.Kerbs.Profile(run.Kind);
            var lines = profile.Select(p => edges.Edge(run.Side, p.Offset, (float)p.Height)).ToArray();
            var strip = Strip.Of(q, lines, run.From, run.To, (line, k) => edges.Pinched(lines[line], run.Side, profile[line].Offset, k));
            if (strip is null) continue;
            var code = run.Kind == KerbKind.Flat ? FlatCode : SawtoothCode;
            var total = strip.Length;
            var cuts = strip.Cuts(Enumerable.Range(1, (int)(total / Course.Kerbs.StripeLength)).Select(k => k * Course.Kerbs.StripeLength));
            for (var c = 0; c + 1 < cuts.Count; c++)
            {
                var (ta, tb) = (cuts[c], cuts[c + 1]);
                var mid = (strip.U(ta) + strip.U(tb)) / 2;
                var colour = (int)(mid / Course.Kerbs.StripeLength) % 2 == 0 ? KerbRed : KerbWhite;
                double Ramp(double t) => Smooth(Math.Min(strip.U(t), total - strip.U(t)) / KerbRamp);
                Facets(m, strip, profile, run.Side, ta, tb, Ramp, colour, code);
            }
        }

        foreach (var run in course.Sausages)
        {
            var profile = Course.Kerbs.SausageProfile;
            var lines = profile.Select(p => edges.Edge(run.Side, Course.Kerbs.SausageOffset + p.Offset, (float)p.Height)).ToArray();
            var strip = Strip.Of(q, lines, run.From, run.To, (line, k) => edges.Pinched(lines[line], run.Side, Course.Kerbs.SausageOffset + profile[line].Offset, k));
            if (strip is null) continue;
            var pitch = Course.Kerbs.SausageModule + Course.Kerbs.SausageGap;
            for (var u0 = 0.0; u0 + Course.Kerbs.SausageModule <= strip.Length; u0 += pitch)
            {
                var u1 = u0 + Course.Kerbs.SausageModule;
                // A module squeezed by a fold would be a sliver: skip it.
                if (strip.Width(strip.T(u0)) < Course.Kerbs.SausageWidth * 0.8 || strip.Width(strip.T(u1)) < Course.Kerbs.SausageWidth * 0.8)
                    continue;
                var cuts = strip.Cuts([u0, u0 + SausageRamp, u1 - SausageRamp, u1]);
                var from = cuts.IndexOf(strip.T(u0));
                var to = cuts.IndexOf(strip.T(u1));
                double Ramp(double t) => Smooth(Math.Min(strip.U(t) - u0, u1 - strip.U(t)) / SausageRamp);
                for (var c = from; c < to; c++)
                    Facets(m, strip, profile, run.Side, cuts[c], cuts[c + 1], Ramp, SausageYellow, SausageCode);
            }
        }
    }

    /// <summary>One flat facet per profile segment between strip positions ta and tb, each with its own vertices.</summary>
    private static void Facets(MeshData m, Strip strip, IReadOnlyList<(double Offset, double Height)> profile, int side,
        double ta, double tb, Func<double, double> ramp, Vector4 colour, float code)
    {
        var ra = ramp(ta);
        var rb = ramp(tb);
        // Nothing where the unfolding pinched the kerb inside a tight bend: it would be a crumpled sliver.
        if ((ra <= 0 && rb <= 0) || strip.Pinched(ta, tb)) return;
        Vector3 P(int p, double t, double r)
        {
            var v = strip.At(p, t);
            // Heights ease down to the verge (0.03 m below the track) at the ends of a run.
            return v with { Y = v.Y - (float)((1 - r) * (profile[p].Height + 0.03)) };
        }
        var data = new Vector4(code, 0, 0, 0);
        float ua = (float)strip.U(ta), ub = (float)strip.U(tb);
        for (var p = 0; p + 1 < profile.Count; p++)
        {
            float oi = (float)profile[p].Offset, oo = (float)profile[p + 1].Offset;
            // A profile segment inside the asphalt edge (negative offset) is the kerb's sealed base joint.
            var c = oo <= 0 ? KerbSeam : colour;
            var ia = m.AddVertex(P(p, ta, ra), new Vector2(ua, oi), c);
            var ib = m.AddVertex(P(p, tb, rb), new Vector2(ub, oi), c);
            var oa = m.AddVertex(P(p + 1, ta, ra), new Vector2(ua, oo), c);
            var ob = m.AddVertex(P(p + 1, tb, rb), new Vector2(ub, oo), c);
            for (var k = 0; k < 4; k++) m.Custom.Add(data);
            // Outwards is "north" (the hi edge) on the left side and "south" on the right.
            if (side > 0) m.AddQuad(oa, ob, ib, ia);
            else m.AddQuad(ia, ib, ob, oa);
        }
    }

    // ---- Run-off --------------------------------------------------------------------------------------------

    internal static void Runoff(MeshData runoff, MeshData gravel, IReadOnlyList<TrackSample> q, EdgeCache edges, RunoffProfile profile)
    {
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        foreach (var side in new[] { +1, -1 })
        {
            var inner = edges.Edge(side, 0, RunoffLift);
            double Mid(int i) => profile.Kind(i, side) == RunoffKind.Gravel ? profile.GravelInner(i, side) : profile.Outer(i, side);
            var mid = edges.Edge(side, Mid, RunoffLift);
            var outer = edges.Edge(side, i => profile.Outer(i, side), RunoffLift);
            var gravelIn = edges.Edge(side, i => profile.GravelInner(i, side), GravelLift);
            var gravelOut = edges.Edge(side, i => profile.Outer(i, side), GravelLift);
            var arc = new double[n + 1];
            for (var k = 1; k <= n; k++)
                arc[k] = arc[k - 1] + Vector3.Distance(inner[k - 1], inner[k]);

            for (var i = 0; i < n; i++)
            {
                var j = i + 1;
                if (profile.Width(i, side) <= 0.01 && profile.Width(j % n, side) <= 0.01) continue;
                var kind = profile.Width(i, side) > 0.01 ? profile.Kind(i, side) : profile.Kind(j % n, side);
                if (kind == RunoffKind.TarmacStriped)
                {
                    // Stripes measured along the inner edge, cut exactly at their boundaries.
                    var (u0, u1) = (arc[i], arc[j]);
                    if (u1 <= u0) continue;
                    var cuts = new List<double> { 0 };
                    for (var b = Math.Ceiling(u0 / RunoffStripe) * RunoffStripe; b < u1; b += RunoffStripe)
                        if (b > u0) cuts.Add((b - u0) / (u1 - u0));
                    cuts.Add(1);
                    for (var c = 0; c + 1 < cuts.Count; c++)
                    {
                        var band = (int)Math.Floor((u0 + (cuts[c] + cuts[c + 1]) / 2 * (u1 - u0)) / RunoffStripe) % 3;
                        var colour = band == 0 ? RunoffRed : band == 1 ? RunoffWhite : RunoffBlue;
                        Cell(runoff, inner, outer, i, j, cuts[c], cuts[c + 1], side, spacing, colour);
                    }
                }
                else
                {
                    Cell(runoff, inner, mid, i, j, 0, 1, side, spacing, Tarmac);
                    if (kind == RunoffKind.Gravel)
                        Cell(gravel, gravelIn, gravelOut, i, j, 0, 1, side, spacing, GravelColour);
                }
            }
        }
    }

    /// <summary>The part fa..fb of the quad between rows i and j of two edges (lo nearer the track, hi further out).</summary>
    private static void Cell(MeshData m, Vector3[] near, Vector3[] far, int i, int j, double fa, double fb, int side, double spacing, Vector4 colour)
    {
        var na = Vector3.Lerp(near[i], near[j], (float)fa);
        var nb = Vector3.Lerp(near[i], near[j], (float)fb);
        var fA = Vector3.Lerp(far[i], far[j], (float)fa);
        var fB = Vector3.Lerp(far[i], far[j], (float)fb);
        if (Vector3.DistanceSquared(na, fA) < 1e-6f && Vector3.DistanceSquared(nb, fB) < 1e-6f) return;
        float sa = (float)((i + fa) * spacing), sb = (float)((i + fb) * spacing);
        // UV = (metres along, metres beyond the kerb): lateral distances from the near edge.
        var ia = m.AddVertex(na, new Vector2(sa, 0), colour);
        var ib = m.AddVertex(nb, new Vector2(sb, 0), colour);
        var oa = m.AddVertex(fA, new Vector2(sa, Vector3.Distance(na, fA)), colour);
        var ob = m.AddVertex(fB, new Vector2(sb, Vector3.Distance(nb, fB)), colour);
        if (side > 0) m.AddQuad(oa, ob, ib, ia);
        else m.AddQuad(ia, ib, ob, oa);
    }

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// A stretch of profile lines between sample rows: position t is a row index (0 = the first row) plus a fraction; U(t) is
    /// the distance along line 0 (the line nearest the asphalt), which never folds before the lines beyond it.
    /// </summary>
    private sealed class Strip
    {
        private readonly Vector3[][] _lines;
        private readonly double[] _u;
        private readonly bool[] _pinched;

        private Strip(Vector3[][] lines, double[] u, bool[] pinched)
        {
            _lines = lines;
            _u = u;
            _pinched = pinched;
        }

        /// <summary>Whether any line is pinched at a row touched by the span ta..tb.</summary>
        public bool Pinched(double ta, double tb)
        {
            var r0 = Math.Clamp((int)Math.Floor(ta), 0, Rows - 1);
            var r1 = Math.Clamp((int)Math.Ceiling(tb), 0, Rows - 1);
            for (var r = r0; r <= r1; r++)
                if (_pinched[r]) return true;
            return false;
        }

        public double Length => _u[^1];
        public int Rows => _u.Length;

        /// <summary>Rows from round(from / spacing) to round(to / spacing) (wrapped onto the lap); null when under two rows.</summary>
        public static Strip? Of(IReadOnlyList<TrackSample> q, Vector3[][] edges, double from, double to, Func<int, int, bool> pinched)
        {
            var n = q.Count;
            var spacing = q[1].S - q[0].S;
            var k0 = (int)Math.Round(from / spacing);
            var k1 = (int)Math.Round(to / spacing);
            if (k1 - k0 < 1) return null;
            var rows = k1 - k0 + 1;
            var lines = new Vector3[edges.Length][];
            for (var p = 0; p < edges.Length; p++)
            {
                lines[p] = new Vector3[rows];
                for (var r = 0; r < rows; r++)
                    lines[p][r] = edges[p][((k0 + r) % n + n) % n];
            }
            var u = new double[rows];
            for (var r = 1; r < rows; r++)
                u[r] = u[r - 1] + Vector3.Distance(lines[0][r - 1], lines[0][r]);
            var flags = new bool[rows];
            for (var r = 0; r < rows; r++)
                for (var p = 0; p < edges.Length && !flags[r]; p++)
                    flags[r] = pinched(p, ((k0 + r) % n + n) % n);
            return u[^1] <= 0 ? null : new Strip(lines, u, flags);
        }

        public Vector3 At(int line, double t)
        {
            var r = Math.Clamp((int)Math.Floor(t), 0, Rows - 2);
            return Vector3.Lerp(_lines[line][r], _lines[line][r + 1], (float)(t - r));
        }

        public double U(double t)
        {
            var r = Math.Clamp((int)Math.Floor(t), 0, Rows - 2);
            return _u[r] + (_u[r + 1] - _u[r]) * (t - r);
        }

        /// <summary>The first position whose U reaches <paramref name="u"/>.</summary>
        public double T(double u)
        {
            if (u <= 0) return 0;
            for (var r = 0; r + 1 < Rows; r++)
                if (_u[r + 1] >= u)
                    return _u[r + 1] > _u[r] ? r + (u - _u[r]) / (_u[r + 1] - _u[r]) : r + 1;
            return Rows - 1;
        }

        /// <summary>Distance across the strip (first to last line) at t.</summary>
        public double Width(double t) => Vector3.Distance(At(0, t), At(_lines.Length - 1, t));

        /// <summary>Every row plus the positions where U crosses each of <paramref name="us"/>, in order and distinct.</summary>
        public List<double> Cuts(IEnumerable<double> us)
        {
            var set = new SortedSet<double>(Enumerable.Range(0, Rows).Select(r => (double)r));
            foreach (var u in us)
                if (u > 0 && u < Length)
                    set.Add(T(u));
            return [.. set];
        }
    }
}
