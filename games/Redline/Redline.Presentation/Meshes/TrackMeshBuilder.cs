using System.Numerics;
using Redline.Presentation.Course;

namespace Redline.Presentation.Meshes;

/// <summary>
/// The track's layers, one mesh (one material) each, plus the <see cref="CourseLayout"/> they were dressed from. See
/// <see cref="TrackMeshBuilder.Build"/>.
/// </summary>
public sealed record TrackMeshes(
    MeshData Asphalt,
    MeshData Lines,
    MeshData Curbs,
    MeshData Runoff,
    MeshData Walls,
    MeshData Fence,
    MeshData Grass,
    MeshData Gravel,
    CourseLayout Course)
{
    /// <summary>The barriers along the track (the walls are drawn only where they are concrete walls).</summary>
    public BarrierLayout? Barriers { get; init; }
}

/// <summary>
/// Builds the circuit's ribbons and walls from <see cref="TrackGeometry.Resample"/>, porting the browser mockup's cota3d.js.
/// Laterals are metres from the centre line, positive to the left of travel. Heights follow the sample's elevation with
/// small lifts so coplanar layers never z-fight: grass -0.03, gravel -0.005, asphalt 0, sealant +0.016, lines and runoff
/// +0.02, painted marks +0.023, chequer +0.025; kerbs follow their own profiles (<see cref="Kerbs.Profile"/>).
/// </summary>
public static class TrackMeshBuilder
{
    /// <summary>Gap from the asphalt edge to the advertising wall.</summary>
    public const double WallGap = 4;
    /// <summary>Catch fence distance from the centre line (pushed out to stay behind the barriers, see <see cref="BarrierLayout"/>).</summary>
    public const double FenceOffset = 19;
    public const double CurbWidth = Kerbs.Width;
    public const double LineWidth = 0.25;
    /// <summary>Gap between the white edge line and the asphalt edge.</summary>
    public const double LineInset = 0.05;
    public const double WallHeight = 1.2;
    /// <summary>Catch fence height (also the top of its overhang at corner outsides, see <see cref="TracksideMeshes"/>).</summary>
    public const double FenceHeight = 4.4;
    /// <summary>Walls and fence reach this far below the track height so no gap shows over the terrain.</summary>
    public const double Footing = 0.3;

    private const float GrassLift = -0.03f, LineLift = 0.02f, ChequerLift = 0.025f;
    private const double ChequerSquare = 0.75;
    private const int ChequerRows = 2;
    // Where the runoff sits, the wall moves out behind it: dilated then box-blurred twice so it ramps smoothly.
    private const int RunoffDilate = 20, RunoffBlur = 10;
    private const double RunoffWallClearance = 1;
    // A wall point may sit this much closer to the centre line than its own offset before it counts as folded or as
    // reaching across to another part of the lap (on the inside of a bend its own sample is exactly the offset away).
    private const double WallClearanceSlack = 0.1;
    // A wall segment must advance at least this fraction of the centre-line step, or it is close to folding.
    private const double MinWallProgress = 0.3;
    // Shortest run of wall segments worth keeping.
    private const int MinWallRun = 5;

    /// <summary>Dusty off-line asphalt; the racing line is darkened in the material from the asphalt's wear data.</summary>
    private static readonly Vector4 AsphaltColour = MeshOps.Hex("#4a4a4b");
    /// <summary>Resurfaced patches are fresher, darker asphalt.</summary>
    private static readonly Vector4 PatchColour = MeshOps.Hex("#38383a");
    private static readonly Vector4 LineColour = MeshOps.Hex("#f1f1ee");
    private static readonly Vector4 WallColour = MeshOps.Hex("#e8e8e8");
    private static readonly Vector4 GrassColour = MeshOps.Hex("#6d8b3a");
    private static readonly Vector4 Black = MeshOps.Hex("#111111");
    private static readonly Vector4 White = MeshOps.Hex("#f4f4f4");

    /// <summary>
    /// Builds every layer for the circuit:
    /// <list type="bullet">
    /// <item><b>Asphalt</b>: a closed ribbon of <c>Width(s)</c>, <c>M + 1</c> rows of (left, right) vertices, the last row repeating
    /// the first, and <c>2M</c> triangles. UV = (s, lateral) in metres, as for every flat layer. Colour is the surface tone
    /// (resurfaced <see cref="Markings.Patches"/> darker); <see cref="MeshData.Custom"/> is the row's sample's
    /// (<see cref="RacingLine.Lateral"/>, <see cref="RacingLine.Rubber"/>, <see cref="RacingLine.Skid"/>,
    /// <see cref="RacingLine.Marbles"/>) on both vertices.</item>
    /// <item><b>Lines</b>: 0.25 m white edge lines on both sides, a chequered start/finish strip across the asphalt at s = 0, and
    /// every <see cref="Markings"/> mark (grid boxes and numbers, pit lines, straight-mode lines, sealant seams).</item>
    /// <item><b>Curbs</b>: the <see cref="Kerbs.Runs"/> and <see cref="Kerbs.Sausages"/> with their profiles, one flat facet per
    /// profile segment and stripe; UV = (metres along the kerb, metres beyond the asphalt edge), Custom.X = kind
    /// (<see cref="CourseMeshes.FlatCode"/>, <see cref="CourseMeshes.SawtoothCode"/>, <see cref="CourseMeshes.SausageCode"/>).</item>
    /// <item><b>Runoff</b>: the <see cref="RunoffProfile"/> areas from the asphalt edge outwards: painted stripes, plain tarmac,
    /// or a tarmac verge before the gravel. UV = (s, metres from the asphalt edge).</item>
    /// <item><b>Gravel</b>: the gravel beds of gravel run-off areas.</item>
    /// <item><b>Walls</b>: one-sided, facing the track, at <c>Width/2 + 4</c> (moved out behind any run-off), 1.2 m tall,
    /// skipped where <see cref="TrackGeometry.WallAllowed"/> is false or where the wall would fold (see <c>VerticalStrip</c>).
    /// UV = (metres along, reading left to right from the track; metres below the top).</item>
    /// <item><b>Fence</b>: catch fence at 19 m from the centre (or 0.5 m behind the wall where that is further), 4.2 m tall,
    /// same facing, UV and fold rules; the material renders it double-sided and translucent.</item>
    /// <item><b>Grass</b>: verge from the asphalt edge to just behind the wall on each side, under kerbs, run-off and gravel.</item>
    /// </list>
    /// Where a flat layer's edge would fold back on itself on the inside of a hairpin (Turn 1's true radius is about 13 m
    /// against a 12 m half-width) that edge pinches to a corner instead, so every triangle keeps its winding.
    /// </summary>
    public static TrackMeshes Build(CircuitData c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var q = TrackGeometry.Resample(c);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var half = new double[n];
        for (var i = 0; i < n; i++)
            half[i] = TrackGeometry.Width(q[i].S) / 2;
        var course = CourseLayout.Build(c, q);
        var barriers = BarrierLayout.Build(q, course, c.StraightModeZones);
        var wall = new Dictionary<int, double[]>
        {
            [+1] = Enumerable.Range(0, n).Select(i => barriers.Line(i, +1)).ToArray(),
            [-1] = Enumerable.Range(0, n).Select(i => barriers.Line(i, -1)).ToArray(),
        };

        // Asphalt: colour is the surface tone (fresh patches darker); Custom carries the racing line and its wear.
        var line = course.RacingLine;
        var patched = new bool[n];
        foreach (var (from, to) in Markings.Patches)
            for (var i = 0; i < n; i++)
                if (q[i].S >= from && q[i].S <= to) patched[i] = true;
        var asphalt = new MeshData();
        Ribbon(asphalt, q, spacing, i => -half[i], i => half[i], 0, i => patched[i] ? PatchColour : AsphaltColour,
            i => new Vector4((float)line.Lateral[i], (float)line.Rubber[i], (float)line.Skid[i], (float)line.Marbles[i]));

        var lines = new MeshData();
        Ribbon(lines, q, spacing, i => half[i] - LineInset - LineWidth, i => half[i] - LineInset, LineLift, LineColour);
        Ribbon(lines, q, spacing, i => -half[i] + LineInset, i => -half[i] + LineInset + LineWidth, LineLift, LineColour);
        Chequer(lines, q, half[0]);
        CourseMeshes.Marks(lines, q, course.Marks);

        var curbs = new MeshData();
        var runoff = new MeshData();
        var gravel = new MeshData();
        var edges = new EdgeCache(q, half);
        var vergeEdge = new Dictionary<(int Side, int Row), Vector3>();
        CourseMeshes.Kerbs(curbs, q, edges, course, vergeEdge);
        CourseMeshes.Runoff(runoff, gravel, q, edges, course.Runoff);

        var walls = new MeshData();
        foreach (var side in new[] { +1, -1 })
        {
            // The concrete wall only where the barrier is a wall: TecPro, tyres and armco stand on its line elsewhere.
            var keep = Enumerable.Range(0, n).Select(i => barriers.Kept(i, side) && barriers.Kind(i, side) == BarrierKind.Wall).ToArray();
            VerticalStrip(walls, q, spacing, side, wall[side], keep, WallHeight, WallColour);
        }
        var fences = TracksideMeshes.Fence(barriers);

        // The inner verge stops short of the fold radius where the wall itself is dropped.
        double Verge(int i, int side)
        {
            var reach = wall[side][i] + 1;
            if (side == Outside(q[i])) return reach;
            return Math.Max(half[i] + 0.5, Math.Min(reach, 0.7 * TrackGeometry.LocalRadius(q[i])));
        }
        var grass = new MeshData();
        // Where a kerb wraps round a hairpin's tip on its own curve, the verge starts at the kerb's outer edge (see Kerbs).
        Ribbon(grass, q, spacing, i => half[i], i => Verge(i, +1), GrassLift, _ => GrassColour, null,
            loEdge: k => vergeEdge.TryGetValue((+1, k), out var v) ? v : null);
        Ribbon(grass, q, spacing, i => -Verge(i, -1), i => -half[i], GrassLift, _ => GrassColour, null,
            hiEdge: k => vergeEdge.TryGetValue((-1, k), out var v) ? v : null);

        // Kerb facets each have their own vertices, so these normals are flat per facet and the bevels stay crisp.
        foreach (var m in new[] { asphalt, lines, curbs, runoff, walls, grass, gravel })
            m.ComputeNormals();
        return new TrackMeshes(asphalt, lines, curbs, runoff, walls, fences, grass, gravel, course) { Barriers = barriers };
    }

    /// <summary>
    /// The asphalt surface point <paramref name="lateral"/> metres left of the centre line at <paramref name="s"/> metres along
    /// the lap (wrapped), linearly interpolating position, tangent and height between samples.
    /// </summary>
    public static Vector3 TrackPoint(IReadOnlyList<TrackSample> samples, double s, double lateral)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var n = samples.Count;
        var spacing = samples[1].S - samples[0].S;
        var lap = spacing * n;
        s %= lap;
        if (s < 0) s += lap;
        var f = s / spacing;
        var i = Math.Min((int)Math.Floor(f), n - 1);
        var k = (float)(f - i);
        var a = samples[i];
        var b = samples[(i + 1) % n];
        var p = Vector2.Lerp(a.Position, b.Position, k);
        var t = Vector2.Normalize(Vector2.Lerp(a.Tangent, b.Tangent, k));
        var left = new Vector2(t.Y, -t.X);
        var h = a.Height + (b.Height - a.Height) * k;
        return new Vector3(p.X + left.X * (float)lateral, (float)h, p.Y + left.Y * (float)lateral);
    }

    /// <summary>+1 when the outside of the bend is the left (a right turn), -1 otherwise.</summary>
    private static int Outside(TrackSample t) => t.Turn > 0 ? 1 : -1;

    internal static Vector3 At(TrackSample t, double lateral, double lift) =>
        new(t.Position.X + t.Left.X * (float)lateral, (float)(t.Height + lift), t.Position.Y + t.Left.Y * (float)lateral);

    /// <summary>
    /// Wall lateral distance per sample on <paramref name="side"/>: Width/2 + 4, or <see cref="RunoffWallClearance"/> behind the
    /// run-off's outer edge where that is further, ramping out ahead of the run-off (dilated, then box-blurred twice).
    /// </summary>
    internal static double[] WallOffsets(IReadOnlyList<TrackSample> q, double[] half, RunoffProfile runoff, int side)
    {
        var n = q.Count;
        var extra = new double[n];
        for (var i = 0; i < n; i++)
            extra[i] = Math.Max(0, runoff.Outer(i, side) + RunoffWallClearance - (half[i] + WallGap));
        var dilated = new double[n];
        for (var i = 0; i < n; i++)
            for (var d = -RunoffDilate; d <= RunoffDilate; d++)
                dilated[i] = Math.Max(dilated[i], extra[((i + d) % n + n) % n]);
        var smooth = BoxBlur(BoxBlur(dilated, RunoffBlur), RunoffBlur);
        var result = new double[n];
        for (var i = 0; i < n; i++)
            result[i] = half[i] + WallGap + Math.Max(extra[i], smooth[i]);
        return result;
    }

    private static double[] BoxBlur(double[] v, int radius)
    {
        var n = v.Length;
        var result = new double[n];
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            for (var d = -radius; d <= radius; d++)
                sum += v[((i + d) % n + n) % n];
            result[i] = sum / (2 * radius + 1);
        }
        return result;
    }

    /// <summary>Edge points of the band between laterals lo &lt; hi for rows k = 0..M (row M repeats row 0), each edge unfolded.</summary>
    internal static (Vector3[] Hi, Vector3[] Lo) Band(IReadOnlyList<TrackSample> q, Func<int, double> lo, Func<int, double> hi, float lift)
    {
        var n = q.Count;
        var h = new Vector3[n + 1];
        var l = new Vector3[n + 1];
        for (var k = 0; k <= n; k++)
        {
            h[k] = At(q[k % n], hi(k % n), lift);
            l[k] = At(q[k % n], lo(k % n), lift);
        }
        Unfold(h, q);
        Unfold(l, q);
        return (h, l);
    }

    /// <summary>
    /// Cuts the swallowtails out of an offset edge (rows 0..M, closed). Where the offset exceeds the local radius on the
    /// inside of a bend the edge runs backwards against the track; that loop is replaced by the point where the edge crosses
    /// itself, so the edge pinches to a corner and every triangle keeps its winding.
    /// </summary>
    internal static void Unfold(Vector3[] e, IReadOnlyList<TrackSample> q)
    {
        var n = q.Count;
        bool Backward(int k)
        {
            var along = q[k % n].Tangent + q[(k + 1) % n].Tangent;
            return (e[k + 1].X - e[k].X) * along.X + (e[k + 1].Z - e[k].Z) * along.Y <= 0;
        }

        for (var a = 0; a < n; a++)
        {
            if (!Backward(a)) continue;
            var b = a;
            while (b + 1 < n && Backward(b + 1)) b++;
            if (!CutLoop(e, q, a, b))
            {
                var x = e[a];
                x.Y = PinchHeight(e, q, x, a, a, b + 1);
                for (var k = a + 1; k <= b + 1; k++)
                    e[k] = x;
            }
            a = b;
        }
        e[n] = e[0];
    }

    /// <summary>
    /// Finds the closest pair of segments (i before backward run a..b, j after it) that cross in the plane and moves every
    /// point between them onto the crossing. The crossing takes the height of the centre-line sample nearest to it among
    /// rows i..j + 1, plus the edge's own lift above the track (the crossing rows can be tens of metres apart on a slope).
    /// False when there is none within reach.
    /// </summary>
    private static bool CutLoop(Vector3[] e, IReadOnlyList<TrackSample> q, int a, int b)
    {
        const int Reach = 60;
        var last = e.Length - 2;
        for (var span = 0; span <= 2 * Reach; span++)
            for (var before = 0; before <= span; before++)
            {
                var i = a - 1 - before;
                var j = b + 1 + (span - before);
                if (i < 0 || j > last) continue;
                if (!Cross(e[i], e[i + 1], e[j], e[j + 1], out var t, out var u)) continue;
                var x = Vector3.Lerp(e[i], e[i + 1], t);
                // The pinch point is on both crossing segments: its height is midway between the edge's heights there, so it
                // stays within the surface either side of the fold (the nearest row's height can be one end of the folded
                // range, tens of centimetres off on a slope, which dents the asphalt and kerbs at a hairpin's tip).
                x.Y = (x.Y + Vector3.Lerp(e[j], e[j + 1], u).Y) / 2;
                for (var k = i + 1; k <= j; k++)
                    e[k] = x;
                return true;
            }
        return false;
    }

    /// <summary>
    /// Height for a pinch point x: the track height at the centre-line sample nearest x among rows from..to, plus the edge's
    /// lift above the track at row <paramref name="liftRow"/>.
    /// </summary>
    private static float PinchHeight(Vector3[] e, IReadOnlyList<TrackSample> q, Vector3 x, int liftRow, int from, int to)
    {
        var n = q.Count;
        var flat = new Vector2(x.X, x.Z);
        var lift = e[liftRow].Y - (float)q[liftRow % n].Height;
        var nearest = from;
        for (var k = from + 1; k <= to; k++)
            if (Vector2.DistanceSquared(q[k % n].Position, flat) < Vector2.DistanceSquared(q[nearest % n].Position, flat))
                nearest = k;
        return (float)q[nearest % n].Height + lift;
    }

    /// <summary>Whether segments p0-p1 and r0-r1 intersect in the XZ plane, with the parameters along each.</summary>
    private static bool Cross(Vector3 p0, Vector3 p1, Vector3 r0, Vector3 r1, out float t, out float u)
    {
        var d1 = new Vector2(p1.X - p0.X, p1.Z - p0.Z);
        var d2 = new Vector2(r1.X - r0.X, r1.Z - r0.Z);
        var w = new Vector2(r0.X - p0.X, r0.Z - p0.Z);
        var den = d1.X * d2.Y - d1.Y * d2.X;
        t = u = 0;
        if (MathF.Abs(den) < 1e-9f) return false;
        t = (w.X * d2.Y - w.Y * d2.X) / den;
        u = (w.X * d1.Y - w.Y * d1.X) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }

    /// <summary>Continuous closed ribbon between laterals lo &lt; hi: M + 1 rows of (hi, lo), the last repeating the first.</summary>
    private static void Ribbon(MeshData m, IReadOnlyList<TrackSample> q, double spacing,
        Func<int, double> lo, Func<int, double> hi, float lift, Vector4 colour) =>
        Ribbon(m, q, spacing, lo, hi, lift, _ => colour, null);

    /// <summary>As above, coloured per row, with optional per-row <see cref="MeshData.Custom"/> data.</summary>
    /// <paramref name="loEdge"/> and <paramref name="hiEdge"/> replace an edge's point at a row (0..M-1) where they give one.
    private static void Ribbon(MeshData m, IReadOnlyList<TrackSample> q, double spacing,
        Func<int, double> lo, Func<int, double> hi, float lift, Func<int, Vector4> colour, Func<int, Vector4>? custom,
        Func<int, Vector3?>? loEdge = null, Func<int, Vector3?>? hiEdge = null)
    {
        var n = q.Count;
        var (h, l) = Band(q, lo, hi, lift);
        for (var k = 0; k <= n; k++)
        {
            if (hiEdge?.Invoke(k % n) is { } he) h[k] = he;
            if (loEdge?.Invoke(k % n) is { } le) l[k] = le;
        }
        int prevHi = -1, prevLo = -1;
        for (var k = 0; k <= n; k++)
        {
            var i = k % n;
            var s = (float)(k * spacing);
            var iHi = m.AddVertex(h[k], new Vector2(s, (float)hi(i)), colour(i));
            var iLo = m.AddVertex(l[k], new Vector2(s, (float)lo(i)), colour(i));
            if (custom is not null)
            {
                var data = custom(i);
                m.Custom.Add(data);
                m.Custom.Add(data);
            }
            // Seen from above, travel runs "east" with the left (hi) edge "north": hi0 → hi1 → lo1 → lo0 is clockwise.
            if (k > 0)
                m.AddQuad(prevHi, iHi, iLo, prevLo);
            prevHi = iHi;
            prevLo = iLo;
        }
    }

    /// <summary>Chequered strip across the asphalt centred on s = 0.</summary>
    private static void Chequer(MeshData m, IReadOnlyList<TrackSample> q, double half)
    {
        var columns = (int)Math.Round(2 * half / ChequerSquare);
        var across = 2 * half / columns;
        for (var r = 0; r < ChequerRows; r++)
        {
            var s0 = (r - ChequerRows / 2.0) * ChequerSquare;
            var s1 = s0 + ChequerSquare;
            for (var col = 0; col < columns; col++)
            {
                var hi = half - col * across;
                var lo = hi - across;
                var c = (r + col) % 2 == 0 ? Black : White;
                var a = m.AddVertex(Lift(TrackPoint(q, s0, hi)), new Vector2((float)s0, (float)hi), c);
                var b = m.AddVertex(Lift(TrackPoint(q, s1, hi)), new Vector2((float)s1, (float)hi), c);
                var d = m.AddVertex(Lift(TrackPoint(q, s1, lo)), new Vector2((float)s1, (float)lo), c);
                var e = m.AddVertex(Lift(TrackPoint(q, s0, lo)), new Vector2((float)s0, (float)lo), c);
                m.AddQuad(a, b, d, e);
            }
        }

        static Vector3 Lift(Vector3 p) => p with { Y = p.Y + ChequerLift };
    }

    /// <summary>
    /// Which segments of a wall, barrier or fence line <paramref name="offsets"/>[i] metres out on <paramref name="side"/>
    /// (+1 left, -1 right) can stand. Segment i → i + 1 is kept only when
    /// <list type="bullet">
    /// <item>both ends pass <see cref="TrackGeometry.WallAllowed"/>;</item>
    /// <item>no centre-line point is closer to either end than its own offset (less <see cref="WallClearanceSlack"/>): the
    /// fold guard's windowed radius is about 25 m at Turn 1 but the true radius about 13 m, so a 16 m inner wall would still
    /// fold there;</item>
    /// <item>the wall advances at least <see cref="MinWallProgress"/> of the centre-line step (an offset close to the true
    /// radius, as at the 11–12 m hairpins of Turns 11 and 15, compresses towards a fold);</item>
    /// <item>it belongs to a run of at least <see cref="MinWallRun"/> such segments, so no stubs are left in a hairpin.</item>
    /// </list>
    /// </summary>
    internal static bool[] KeptSegments(IReadOnlyList<TrackSample> q, SampleGrid grid, int side, double[] offsets)
    {
        var n = q.Count;
        var foot = new Vector2[n];
        var clear = new bool[n];
        for (var i = 0; i < n; i++)
        {
            foot[i] = q[i].Position + q[i].Left * (float)(side * offsets[i]);
            clear[i] = TrackGeometry.WallAllowed(q[i], side, offsets[i])
                && grid.Nearest(foot[i].X, foot[i].Y).Distance > offsets[i] - WallClearanceSlack;
        }

        var keep = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var along = Vector2.Normalize(q[i].Tangent + q[j].Tangent);
            var step = Vector2.Distance(q[i].Position, q[j].Position);
            keep[i] = clear[i] && clear[j] && Vector2.Dot(foot[j] - foot[i], along) > MinWallProgress * step;
        }
        DropShortRuns(keep, MinWallRun);
        return keep;
    }

    /// <summary>
    /// A vertical ribbon <paramref name="offsets"/>[i] metres out on <paramref name="side"/> from <see cref="Footing"/> below
    /// the track to <paramref name="height"/> above it, facing the track, over the segments <paramref name="keep"/> marks
    /// (from <see cref="KeptSegments"/>). Consecutive kept segments share vertices.
    /// </summary>
    private static void VerticalStrip(MeshData m, IReadOnlyList<TrackSample> q, double spacing, int side,
        double[] offsets, bool[] keep, double height, Vector4 colour)
    {
        var n = q.Count;
        var lastK = -1;
        int lastTop = -1, lastBottom = -1;
        for (var k = 0; k < n; k++)
        {
            if (!keep[k]) continue;
            if (lastK != k)
                (lastTop, lastBottom) = Column(k);
            var (top, bottom) = Column(k + 1);
            // Seen from the track, a left wall runs with travel to the viewer's right, a right wall to the viewer's left.
            if (side > 0)
                m.AddQuad(lastTop, top, bottom, lastBottom);
            else
                m.AddQuad(top, lastTop, lastBottom, bottom);
            (lastK, lastTop, lastBottom) = (k + 1, top, bottom);
        }

        (int Top, int Bottom) Column(int k)
        {
            var i = k % n;
            var lateral = side * offsets[i];
            var u = (float)(side * k * spacing);
            var top = m.AddVertex(At(q[i], lateral, height), new Vector2(u, 0), colour);
            var bottom = m.AddVertex(At(q[i], lateral, -Footing), new Vector2(u, (float)(height + Footing)), colour);
            return (top, bottom);
        }
    }

    /// <summary>Clears every run of true values (on the closed loop) shorter than <paramref name="minRun"/>.</summary>
    private static void DropShortRuns(bool[] keep, int minRun)
    {
        var n = keep.Length;
        var start = Array.IndexOf(keep, false);
        if (start < 0) return;
        var run = 0;
        for (var step = 1; step <= n; step++)
        {
            var i = (start + step) % n;
            if (keep[i] && step < n)
            {
                run++;
                continue;
            }
            if (run < minRun)
                for (var b = 1; b <= run; b++)
                    keep[((i - b) % n + n) % n] = false;
            run = 0;
        }
    }
}

/// <summary>Spatial hash of track samples for nearest-centre-line queries in the circuit plane.</summary>
internal sealed class SampleGrid
{
    private const float Bucket = 25;
    private readonly IReadOnlyList<TrackSample> _q;
    private readonly Dictionary<(int, int), List<int>> _cells = new();

    public SampleGrid(IReadOnlyList<TrackSample> q)
    {
        _q = q;
        for (var i = 0; i < q.Count; i++)
        {
            var key = Key(q[i].Position.X, q[i].Position.Y);
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = new List<int>();
            list.Add(i);
        }
    }

    /// <summary>
    /// The sample nearest (x, z) and its distance, or (-1, +∞) when none is within <paramref name="maxDistance"/>.
    /// </summary>
    public (int Index, double Distance) Nearest(double x, double z, double maxDistance = 1e6)
    {
        var (cx, cz) = Key(x, z);
        var maxRing = (int)Math.Ceiling(maxDistance / Bucket) + 1;
        var best = double.MaxValue;
        var index = -1;
        for (var r = 0; r <= maxRing; r++)
        {
            foreach (var i in Ring(cx, cz, r))
            {
                var d2 = Dist2(i, x, z);
                if (d2 < best) (best, index) = (d2, i);
            }
            // Anything in ring r + 1 or beyond is at least r buckets away.
            if (best <= Sq(r * Bucket)) break;
        }
        var d = Math.Sqrt(best);
        return d <= maxDistance ? (index, d) : (-1, double.PositiveInfinity);
    }

    public double Dist2(int i, double x, double z) => Sq(_q[i].Position.X - x) + Sq(_q[i].Position.Y - z);

    private IEnumerable<int> Ring(int cx, int cz, int r)
    {
        for (var dx = -r; dx <= r; dx++)
            for (var dz = -r; dz <= r; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                if (_cells.TryGetValue((cx + dx, cz + dz), out var list))
                    foreach (var i in list)
                        yield return i;
            }
    }

    private static (int, int) Key(double x, double z) => ((int)Math.Floor(x / Bucket), (int)Math.Floor(z / Bucket));

    private static double Sq(double v) => v * v;
}
