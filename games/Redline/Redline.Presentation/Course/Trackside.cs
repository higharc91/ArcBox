using System.Numerics;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace Redline.Presentation.Course;

/// <summary>
/// What the trackside is planned round: the track's samples and meshes (its <see cref="TrackMeshes.Course"/> and
/// <see cref="TrackMeshes.Barriers"/>), the rendered ground, the lap length and the landmarks' structures (grandstands,
/// pit building, tower) as rectangles nothing may stand on.
/// </summary>
public sealed record TracksideSite(
    IReadOnlyList<TrackSample> Samples,
    TrackMeshes Track,
    TerrainSampler Ground,
    double LapLength,
    IReadOnlyList<Obb> Structures)
{
    public BarrierLayout Barriers => Track.Barriers ?? throw new InvalidOperationException("The track was built without its barriers.");

    public double PitS => LapLength - Surroundings.PitFromLapEnd;

    private SurfaceIndex? _verge;

    /// <summary>The verge as rendered (<see cref="TrackMeshes.Grass"/>), for heights over it.</summary>
    public SurfaceIndex Verge => _verge ??= new SurfaceIndex(Track.Grass);
}

/// <summary>
/// A numbered sign: the prop, its <see cref="TracksideAtlas"/> cell, the turn it belongs to (0 for a marshal post), where
/// along the lap and on which side (+1 left) it stands, and its number as drawn.
/// </summary>
public readonly record struct Sign(Prop Prop, int Cell, int Turn, double S, int Side, string Text);

/// <summary>
/// Everything <see cref="Trackside.Plan"/> places. Each instanced list is one MultiMesh (TecPro, tyre modules, armco posts,
/// fence posts) or a few (the furniture); <see cref="Prop.Variant"/> picks the model node where a list has more than one
/// (fence posts: 0 straight, 1 overhang), <see cref="Prop.Shade"/> carries the per-instance data (TecPro: the colour
/// index 0 red, 1 white, 2 blue; an overhang post: how far it leans, its local Z scale).
/// </summary>
public sealed record TracksidePlan(
    BarrierLayout Barriers,
    IReadOnlyList<Prop> TecPro,
    IReadOnlyList<Prop> Tyres,
    IReadOnlyList<Prop> ArmcoPosts,
    IReadOnlyList<Prop> FencePosts,
    IReadOnlyList<Sign> MarshalPosts,
    IReadOnlyList<Sign> CornerBoards,
    IReadOnlyList<Sign> BrakeBoards,
    IReadOnlyList<Prop> CameraPlatforms,
    Prop BigScreen,
    MeshData Rails,
    MeshData Cables)
{
    /// <summary>Every barrier prop's footprint with what it is, for the overlap rules.</summary>
    public IEnumerable<(string What, Obb Box)> BarrierFootprints() =>
        TecPro.Select((p, i) => ($"tecpro {i}", p.Footprint))
            .Concat(Tyres.Select((p, i) => ($"tyres {i}", p.Footprint)))
            .Concat(ArmcoPosts.Select((p, i) => ($"armco post {i}", p.Footprint)));

    /// <summary>Every piece of furniture's footprint with what it is.</summary>
    public IEnumerable<(string What, Obb Box)> FurnitureFootprints() =>
        MarshalPosts.Select(m => ($"marshal post {m.Text}", m.Prop.Footprint))
            .Concat(CornerBoards.Select(b => ($"corner board {b.Text}", b.Prop.Footprint)))
            .Concat(BrakeBoards.Select(b => ($"brake board T{b.Turn} {b.Text}", b.Prop.Footprint)))
            .Concat(CameraPlatforms.Select((p, i) => ($"camera platform {i}", p.Footprint)))
            .Append(("big screen", BigScreen.Footprint));
}

/// <summary>
/// The cells of the number atlas the TV draws once (<c>World/Trackside</c>) and the boards read per instance:
/// T1..T20 (corner boards), 100/200/300 (brake boards), then the marshal posts' numbers from 1.
/// </summary>
public static class TracksideAtlas
{
    public const int Columns = 8, Rows = 7;
    public const int Turns = 20;
    public const int BrakeFirst = Turns;
    public const int PostFirst = BrakeFirst + 3;

    public static int Corner(int turn) => turn - 1;

    public static int Brake(int metres) => BrakeFirst + metres / 100 - 1;

    public static int Post(int number) => PostFirst + number - 1;

    /// <summary>The text of every cell, in order.</summary>
    public static IReadOnlyList<string> Cells { get; } =
    [
        .. Enumerable.Range(1, Turns).Select(t => $"T{t}"),
        "100", "200", "300",
        .. Enumerable.Range(1, Columns * Rows - PostFirst).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];
}

/// <summary>
/// Plans the barriers' props and the trackside furniture (spec §2.3) from the corner data and the
/// <see cref="BarrierLayout"/>:
/// <list type="bullet">
/// <item>TecPro blocks (<see cref="TecProPitch"/>, red/white/blue in turn) and tyre modules (<see cref="TyrePitch"/>)
/// along their runs' barrier line, each on the chord between its ends, face on the line; armco posts at every row of an
/// armco run (the rails are <see cref="TracksideMeshes.Rails"/>).</item>
/// <item>Catch fence posts every <see cref="FencePostRows"/> rows of kept fence, leaning where the fence does.</item>
/// <item>Marshal posts every <see cref="MarshalSpacing"/> metres on alternating sides behind the barrier (between it and
/// the fence where there is room, else behind the fence), numbered in lap order; a post that would land on the pit lane,
/// a landmark or another part of the track moves up to <see cref="MarshalShift"/> metres or to the other side.</item>
/// <item>Corner boards T1–T20 behind the fence on each corner's outside before its entry, turned to the approaching cars;
/// brake boards 300/200/100 metres before the entry of every Medium or Heavy braking corner on its outside, between the
/// asphalt's run-off edge and the barrier (or behind the barrier where there is no room), facing the approaching cars,
/// left out where the distance reaches back past the previous corner.</item>
/// <item>TV camera platforms behind the fence at <see cref="CameraTurns"/>, looking at the apex; the big screen at the
/// end of the main grandstand, looking back up the straight.</item>
/// </list>
/// Nothing stands inside <see cref="RunoffProfile.Outer"/> of any part of the track, on the pit lane or on a landmark, and
/// every prop stands on the ground or the verge (barriers at the track's height on the verge, furniture on the lowest
/// ground under its footprint, so no corner floats). Deterministic. The lists are interleaved (every other instance
/// first), so drawing the first half of a list halves its density evenly (the Low preset).
/// </summary>
public static class Trackside
{
    public const double TecProPitch = 1.5, TyrePitch = 1.8;
    public const int FencePostRows = 2;
    public const double MarshalSpacing = 300, MarshalFirst = 150, MarshalShift = 50;
    public static readonly int[] CameraTurns = [1, 11, 12, 15];
    /// <summary>The big screen stands this far before the start line, on the main grandstand's side (right).</summary>
    public const double ScreenFromLapEnd = 32;
    /// <summary>Barriers stand on the verge, this far below the track (as <c>TrackMeshBuilder</c>'s verge).</summary>
    public const float VergeLift = -0.03f;
    /// <summary>How far the barrier props and the furniture reach below their origin (tools/blender/trackside.py).</summary>
    public const float BarrierFooting = 0.45f, Footing = 0.25f, FenceFooting = (float)TracksideMeshes.FenceFooting;

    // Footprints (local X along, Z from the front face back), from tools/blender/trackside.py.
    public const float TecProHalf = 0.74f, TecProDeep = 0.95f;
    public const float TyreHalf = 0.9f, TyreDeep = 1.25f;
    public const float ArmcoHalf = 0.075f, ArmcoDeep = 0.3f;
    public const float FencePostRadius = 0.06f;
    public const float MarshalHalf = 1.5f, MarshalFront = 0.45f, MarshalBack = 2.15f, MarshalPanel = 1.95f;
    public const float CornerBoardHalf = 1.26f, CornerBoardDeep = 0.18f;
    public const float BrakeBoardHalf = 0.64f, BrakeBoardDeep = 0.12f;
    public const float CameraHalf = 1.35f;
    public const float ScreenHalf = 6.35f, ScreenDeep = 2.1f;

    /// <summary>Gap kept between a prop and the run-off edge, a barrier or the fence.</summary>
    public const double Margin = 0.3;

    public static TracksidePlan Plan(TracksideSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var w = new Placer(site);
        var b = site.Barriers;
        var tecpro = new List<Prop>();
        var tyres = new List<Prop>();
        var posts = new List<Prop>();
        foreach (var run in b.Runs)
        {
            switch (run.Kind)
            {
                case BarrierKind.TecPro:
                    var colour = 0;
                    foreach (var (centre, axis, _) in w.Chords(run, TecProPitch))
                        tecpro.Add(Placer.Barrier(centre, axis, Surface(site, centre), TecProHalf, TecProDeep, colour++ % 3));
                    break;
                case BarrierKind.Tyres:
                    foreach (var (centre, axis, _) in w.Chords(run, TyrePitch))
                        tyres.Add(Placer.Barrier(centre, axis, Surface(site, centre), TyreHalf, TyreDeep, 0));
                    break;
                case BarrierKind.Armco:
                    for (var k = run.From; k <= run.To; k++)
                    {
                        var foot = b.Foot(k, run.Side);
                        var at = new Vector2(foot.X, foot.Z);
                        posts.Add(Placer.Barrier(at, w.Along(k, run.Side), Surface(site, at), ArmcoHalf, ArmcoDeep, 0));
                    }
                    break;
            }
        }

        var fencePosts = new List<Prop>();
        var n = site.Samples.Count;
        foreach (var side in new[] { +1, -1 })
            for (var k = 0; k < n; k += FencePostRows)
            {
                if (!b.FenceKept(k, side) && !b.FenceKept(k - 1, side)) continue;
                var lean = (float)b.Overhang(k, side);
                var foot = TrackMeshBuilder.At(site.Samples[k], side * b.Fence(k, side), 0);
                var outward = w.Outward(k, side);
                var axis = new Vector2(outward.Y, -outward.X);
                var at = new Vector2(foot.X, foot.Z);
                fencePosts.Add(new Prop(lean > 0.05f ? 1 : 0, foot with { Y = foot.Y + VergeLift }, Yaw(axis), 1, lean,
                    new Obb(at, axis, FencePostRadius, FencePostRadius)));
            }

        var marshals = w.MarshalPosts(tecpro.Concat(tyres).Concat(posts).Select(p => p.Footprint).ToList());
        var placed = marshals.Select(m => m.Prop.Footprint).ToList();
        var cornerBoards = w.CornerBoards(placed);
        placed.AddRange(cornerBoards.Select(c => c.Prop.Footprint));
        var brakeBoards = w.BrakeBoards(placed);
        placed.AddRange(brakeBoards.Select(c => c.Prop.Footprint));
        var cameras = w.Cameras(placed);
        placed.AddRange(cameras.Select(c => c.Footprint));
        var screen = w.Screen(placed);

        return new TracksidePlan(b, Interleave(tecpro), Interleave(tyres), Interleave(posts), Interleave(fencePosts),
            marshals, cornerBoards, brakeBoards, cameras, screen, TracksideMeshes.Rails(b), TracksideMeshes.Cables(b));
    }

    /// <summary>
    /// A grandstand's ground footprint from its frame (origin at the middle of its front edge on the ground,
    /// <paramref name="axis"/> its length, rows climbing away to the left of it): from 0.5 m in front of the first row to
    /// 8 m behind the last (stairs, back wall, roof columns).
    /// </summary>
    public static Obb Stand(Vector2 origin, Vector2 axis, float length, int rows)
    {
        var depth = (rows * 1.2f) + 8.5f;
        var across = new Vector2(-axis.Y, axis.X);
        return new Obb(origin + across * ((depth / 2) - 0.5f), axis, length / 2, depth / 2);
    }

    /// <summary>
    /// The landmarks' structures as rectangles nothing may stand on, from the frames the TV's <c>World/Landmarks</c> uses:
    /// the main grandstand (205 m before the line, 21 m right), the Turn 1 grandstand (s = 335, 32 m outside), the three
    /// corner grandstands on the sharpest corners after Turn 1 whose footprints keep 22 m from the lap, and the Observation
    /// Tower's base. (The pit building is the pit zone, <see cref="InPitZone(TracksideSite, Vector2)"/>.)
    /// </summary>
    public static IReadOnlyList<Obb> Structures(CircuitData c, IReadOnlyList<TrackSample> q)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(q);
        var lap = (float)c.LapLength;
        (Vector2 O, Vector2 X) Frame(float s, float lateral, int side)
        {
            var pose = TrackPose.At(q, s, lateral);
            var fwd = Vector2.Normalize(new Vector2(pose.Forward.X, pose.Forward.Z));
            return (new Vector2(pose.Position.X, pose.Position.Z), -side * fwd);
        }
        var list = new List<Obb>();
        var main = Frame(lap - 205, -21, -1);
        list.Add(Stand(main.O, main.X, LandmarkModels.MainStand.Length, LandmarkModels.MainStand.Rows));
        var i1 = (int)Math.Round(335 / TrackGeometry.Step) % q.Count;
        var out1 = q[i1].Turn > 0 ? 1 : -1;
        var t1 = Frame(335, out1 * ((float)TrackGeometry.Width(335) / 2 + 32), out1);
        list.Add(Stand(t1.O, t1.X, LandmarkModels.TurnOneStand.Length, LandmarkModels.TurnOneStand.Rows));
        var picked = new List<double>();
        foreach (var i in Enumerable.Range(0, q.Count).OrderByDescending(i => q[i].Curvature))
        {
            var s = q[i].S;
            if (s < 700 || s > lap - 800 || picked.Any(p => Math.Abs(p - s) < 600))
                continue;
            var outside = q[i].Turn > 0 ? 1 : -1;
            var xf = Frame((float)s, outside * ((float)TrackGeometry.Width(s) / 2 + 30), outside);
            var across = new Vector2(-xf.X.Y, xf.X.X);
            var len = LandmarkModels.CornerStand.Length;
            var clear = new[] { -1f, 1f }.SelectMany(x => new[] { 0f, 22f }.Select(z => xf.O + (xf.X * (x * len / 2)) + (across * z)))
                .All(k => q.Min(t => Vector2.Distance(t.Position, k)) > 22);
            if (!clear)
                continue;
            list.Add(Stand(xf.O, xf.X, len, LandmarkModels.CornerStand.Rows));
            picked.Add(s);
            if (picked.Count == 3)
                break;
        }
        var pts = c.Points.Skip(128).Take(24).ToList();
        var tower = new Vector2(pts.Average(p => p.X), pts.Average(p => p.Y));
        list.Add(new Obb(tower, Vector2.UnitX, Surroundings.TowerStructure, Surroundings.TowerStructure));
        return list;
    }

    /// <summary>The marshal posts' spacing on a lap: the nearest to <see cref="MarshalSpacing"/> that divides it evenly.</summary>
    public static double Spacing(double lap) => lap / Math.Max(1, Math.Round(lap / MarshalSpacing));

    /// <summary>Every other item first, then the rest: a prefix of half the list is every second item.</summary>
    public static IReadOnlyList<T> Interleave<T>(IReadOnlyList<T> items) =>
        [.. items.Where((_, i) => i % 2 == 0), .. items.Where((_, i) => i % 2 == 1)];

    /// <summary>The Godot yaw that turns a model's local +X onto <paramref name="axis"/> (its +Z then points across).</summary>
    public static float Yaw(Vector2 axis) => MathF.Atan2(-axis.Y, axis.X);

    /// <summary>
    /// The ground a footprint point stands on as rendered: the verge (<see cref="TracksideSite.Verge"/>) where it reaches,
    /// else the terrain (the higher of the two where both are drawn).
    /// </summary>
    public static float Surface(TracksideSite site, Vector2 p)
    {
        ArgumentNullException.ThrowIfNull(site);
        var g = site.Ground.Height(p.X, p.Y);
        return site.Verge.Height(p) is { } v ? MathF.Max(g, v) : g;
    }

    /// <summary>
    /// How far <paramref name="p"/> is outside the run-off of the nearest part of the track (negative inside it):
    /// distance from the nearest centre-line sample less that sample's <see cref="RunoffProfile.Outer"/> on p's side.
    /// </summary>
    public static double Clearance(TracksideSite site, Vector2 p) => Clearance(site, p, null);

    internal static double Clearance(TracksideSite site, Vector2 p, SampleGrid? grid)
    {
        ArgumentNullException.ThrowIfNull(site);
        var (i, d) = (grid ?? new SampleGrid(site.Samples)).Nearest(p.X, p.Y);
        var t = site.Samples[i];
        var side = Vector2.Dot(p - t.Position, t.Left) >= 0 ? 1 : -1;
        return d - site.Track.Course.Runoff.Outer(i, side);
    }

    /// <summary>Whether <paramref name="p"/> is on the pit lane, its building or the paddock behind (left of the pit straight).</summary>
    public static bool InPitZone(TracksideSite site, Vector2 p) => InPitZone(site, p, null);

    internal static bool InPitZone(TracksideSite site, Vector2 p, SampleGrid? grid)
    {
        ArgumentNullException.ThrowIfNull(site);
        var (i, d) = (grid ?? new SampleGrid(site.Samples)).Nearest(p.X, p.Y);
        var t = site.Samples[i];
        var side = Vector2.Dot(p - t.Position, t.Left) >= 0 ? 1 : -1;
        var ds = StraightModeZone.Wrap(t.S - site.PitS + Surroundings.PitZoneBefore, site.LapLength);
        return side > 0 && ds <= Surroundings.PitZoneBefore + Surroundings.PitZoneAfter && d >= Surroundings.PitZoneInner - 1;
    }

    private sealed class Placer(TracksideSite site)
    {
        private readonly SampleGrid _grid = new(site.Samples);
        private readonly IReadOnlyList<TrackSample> _q = site.Samples;
        private readonly BarrierLayout _b = site.Barriers;
        private double Spacing => _q[1].S - _q[0].S;
        private int N => _q.Count;

        private int Row(double s) => (((int)Math.Round(s / Spacing)) % N + N) % N;

        /// <summary>The level unit direction away from the track at row k on <paramref name="side"/>.</summary>
        public Vector2 Outward(int k, int side) => side * _q[((k % N) + N) % N].Left;

        /// <summary>The model's local +X for a barrier at row k: Outward turned so that +Z = Outward.</summary>
        public Vector2 Along(int k, int side)
        {
            var o = Outward(k, side);
            return new Vector2(o.Y, -o.X);
        }

        /// <summary>
        /// Pieces <paramref name="pitch"/> long along a run's barrier line, each the chord between two points that far apart
        /// along the line: its middle, the unit direction along it (local +X, so +Z points away from the track) and the
        /// height of the verge there.
        /// </summary>
        public IEnumerable<(Vector2 Centre, Vector2 Axis, float Y)> Chords(BarrierRun run, double pitch)
        {
            var pts = new List<Vector3>();
            for (var k = run.From; k <= run.To; k++)
                pts.Add(_b.Foot(k, run.Side));
            var arc = new double[pts.Count];
            for (var i = 1; i < pts.Count; i++)
                arc[i] = arc[i - 1] + Vector2.Distance(Flat(pts[i - 1]), Flat(pts[i]));
            var count = (int)Math.Floor(arc[^1] / pitch);
            if (count < 1) yield break;
            // Centre the pieces on the run: the slack splits between its ends.
            var start = (arc[^1] - count * pitch) / 2;
            for (var c = 0; c < count; c++)
            {
                var a = At(pts, arc, start + c * pitch);
                var e = At(pts, arc, start + (c + 1) * pitch);
                var dir = Flat(e) - Flat(a);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector2.Normalize(dir);
                // +Z (Across) must point away from the track: flip the axis on the left side if needed.
                var across = new Vector2(-dir.Y, dir.X);
                var k = run.From + (int)Math.Round((start + (c + 0.5) * pitch) / Math.Max(arc[^1], 1e-6) * (pts.Count - 1));
                if (Vector2.Dot(across, Outward(k, run.Side)) < 0) dir = -dir;
                yield return ((Flat(a) + Flat(e)) / 2, dir, (a.Y + e.Y) / 2 + VergeLift);
            }
        }

        private static Vector3 At(List<Vector3> pts, double[] arc, double a)
        {
            var i = Array.BinarySearch(arc, a);
            if (i < 0) i = ~i;
            i = Math.Clamp(i, 1, pts.Count - 1);
            var f = arc[i] > arc[i - 1] ? (a - arc[i - 1]) / (arc[i] - arc[i - 1]) : 0;
            return Vector3.Lerp(pts[i - 1], pts[i], (float)Math.Clamp(f, 0, 1));
        }

        /// <summary>A barrier prop whose face's middle is at <paramref name="face"/>, reaching <paramref name="deep"/> back.</summary>
        public static Prop Barrier(Vector2 face, Vector2 axis, float y, float half, float deep, int shade)
        {
            var across = new Vector2(-axis.Y, axis.X);
            var box = new Obb(face + across * (deep / 2), axis, half, deep / 2);
            return new Prop(0, new Vector3(face.X, y, face.Y), Yaw(axis), 1, shade, box);
        }

        /// <summary>Whether a footprint is clear of the run-off of every part of the track, the pit zone and the landmarks.</summary>
        public bool Clear(Obb box, double margin)
        {
            foreach (var c in box.Corners.Append(box.Centre))
            {
                if (Clearance(site, c, _grid) < margin) return false;
                if (InPitZone(site, c, _grid)) return false;
            }
            // Also the middles of the long edges (a long prop's corners can straddle the run-off of a bend).
            for (var f = -1f; f <= 1f; f += 0.5f)
                foreach (var v in new[] { -1f, 1f })
                    if (Clearance(site, box.At(f * box.HalfLength, v * box.HalfWidth), _grid) < margin) return false;
            return !site.Structures.Any(s => s.Overlaps(box, 1));
        }

        /// <summary>Whether a footprint stays off every barrier (their props and the barrier line's band) and the fence.</summary>
        public bool OffBarriers(Obb box, IReadOnlyList<Obb> taken)
        {
            if (taken.Any(t => Vector2.Distance(t.Centre, box.Centre) < t.Reach + box.Reach + 1 && t.Overlaps(box, (float)Margin)))
                return false;
            foreach (var c in box.Corners.Append(box.Centre))
            {
                var (i, d) = _grid.Nearest(c.X, c.Y);
                var t = _q[i];
                var side = Vector2.Dot(c - t.Position, t.Left) >= 0 ? 1 : -1;
                var line = _b.Line(i, side);
                var back = line + BarrierLayout.Depth(_b.Kind(i, side));
                if (d > line - Margin && d < back + Margin) return false;
                var fence = _b.Fence(i, side);
                if (_b.FenceKept(i, side) && Math.Abs(d - fence) < Margin + 0.1) return false;
            }
            return true;
        }

        /// <summary>Height for furniture on <paramref name="box"/>: the lowest ground under its corners and middle.</summary>
        public float Ground(Obb box) => box.Corners.Append(box.Centre).Min(c => Surface(site, c));

        /// <summary>A prop standing at row k on side, its front <paramref name="lateral"/> from the centre line, facing the track.</summary>
        private Prop Facing(int variant, int k, int side, double lateral, float half, float front, float back, float shade)
        {
            var t = _q[((k % N) + N) % N];
            var origin = t.Position + side * t.Left * (float)(lateral + front);
            var outward = Outward(k, side);
            var axis = new Vector2(outward.Y, -outward.X);
            var box = new Obb(origin + outward * ((back - front) / 2), axis, half, (back + front) / 2);
            return new Prop(variant, new Vector3(origin.X, Ground(box), origin.Y), Yaw(axis), 1, shade, box);
        }

        public List<Sign> MarshalPosts(List<Obb> taken)
        {
            var result = new List<Sign>();
            var side = +1;
            var spacing = Spacing(site.LapLength);
            var count = (int)Math.Round(site.LapLength / spacing);
            for (var m = 0; m < count; m++)
            {
                var s = MarshalFirst + m * spacing;
                Prop? found = null;
                var foundSide = side;
                var foundS = s;
                foreach (var sd in new[] { side, -side })
                {
                    foreach (var shift in Shifts(MarshalShift, 10))
                    {
                        var k = Row(s + shift);
                        if (!_b.Kept(k, sd)) continue;
                        var back = _b.Line(k, sd) + BarrierLayout.Depth(_b.Kind(k, sd)) + Margin + 0.2;
                        var fence = _b.Fence(k, sd);
                        // Between the barrier and the fence where it fits, else behind the fence.
                        var lateral = back + MarshalFront + MarshalBack + Margin < fence ? back : fence + 0.8;
                        var p = Facing(0, k, sd, lateral, MarshalHalf + 0.6f, MarshalFront, MarshalBack, 0);
                        // The light panel's pole stands beside the hut: keep its box wide enough to cover it.
                        if (!Clear(p.Footprint, 1.0) || !OffBarriers(p.Footprint, taken)) continue;
                        if (Math.Abs(Ground(p.Footprint) - p.Footprint.Corners.Max(c => Surface(site, c))) > 1.2) continue;
                        (found, foundSide, foundS) = (p, sd, s + shift);
                        break;
                    }
                    if (found is not null) break;
                }
                if (found is { } prop)
                {
                    var number = result.Count + 1;
                    result.Add(new Sign(prop, TracksideAtlas.Post(number), 0, StraightModeZone.Wrap(foundS, site.LapLength), foundSide,
                        number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    taken.Add(prop.Footprint);
                }
                side = -side;
            }
            return result;
        }

        public List<Sign> CornerBoards(List<Obb> taken)
        {
            var result = new List<Sign>();
            foreach (var span in site.Track.Course.Corners)
            {
                var c = span.Corner;
                foreach (var shift in Shifts(30, 5).Select(x => x - 20))
                {
                    var s = span.Entry + shift;
                    var k = Row(s);
                    var side = c.OutsideSide;
                    var t = _q[k];
                    var lateral = _b.Fence(k, side) + 1.6;
                    var at = t.Position + side * t.Left * (float)lateral;
                    // Turned half-way between the approaching cars and the track: its face looks back and in.
                    var outward = Vector2.Normalize(t.Tangent + Outward(k, side));
                    var axis = new Vector2(outward.Y, -outward.X);
                    var box = new Obb(at + outward * (CornerBoardDeep / 2), axis, CornerBoardHalf, CornerBoardDeep / 2);
                    if (!Clear(box, 1.0) || !OffBarriers(box, taken)) continue;
                    var prop = new Prop(0, new Vector3(at.X, Ground(box), at.Y), Yaw(axis), 1, 0, box);
                    result.Add(new Sign(prop, TracksideAtlas.Corner(c.Turn), c.Turn, StraightModeZone.Wrap(s, site.LapLength), side, $"T{c.Turn}"));
                    taken.Add(box);
                    break;
                }
            }
            return result;
        }

        public List<Sign> BrakeBoards(List<Obb> taken)
        {
            var result = new List<Sign>();
            var spans = site.Track.Course.Corners;
            for (var ci = 0; ci < spans.Count; ci++)
            {
                var span = spans[ci];
                var c = span.Corner;
                if (c.Braking is not (Braking.Medium or Braking.Heavy)) continue;
                var prev = spans[(ci - 1 + spans.Count) % spans.Count];
                var room = StraightModeZone.Wrap(span.Entry - prev.Exit, site.LapLength);
                foreach (var metres in new[] { 300, 200, 100 })
                {
                    if (metres > room - 20) continue;
                    var s = span.Entry - metres;
                    var side = c.OutsideSide;
                    var pos = TrackMeshBuilder.TrackPoint(_q, s, 0);
                    var ahead = TrackMeshBuilder.TrackPoint(_q, s + 1, 0);
                    var fwd = Vector2.Normalize(new Vector2(ahead.X - pos.X, ahead.Z - pos.Z));
                    var left = new Vector2(fwd.Y, -fwd.X);
                    var k = Row(s);
                    var outer = site.Track.Course.Runoff.Outer(k, side);
                    var line = _b.Line(k, side);
                    var lateral = (outer + line) / 2;
                    if (line - outer < 2 * (BrakeBoardHalf + Margin) + 0.1)
                        lateral = line + BarrierLayout.Depth(_b.Kind(k, side)) + Margin + BrakeBoardHalf + 0.1;
                    var at = new Vector2(pos.X, pos.Z) + side * left * (float)lateral;
                    // Its face looks back at the approaching cars: local +Z along travel.
                    var axis = new Vector2(fwd.Y, -fwd.X);
                    var box = new Obb(at + fwd * (BrakeBoardDeep / 2), axis, BrakeBoardHalf, BrakeBoardDeep / 2);
                    if (!Clear(box, Margin) || taken.Any(o => o.Overlaps(box, (float)Margin))) continue;
                    var y = Surface(site, at);
                    var prop = new Prop(0, new Vector3(at.X, MathF.Min(y, Ground(box)), at.Y), Yaw(axis), 1, 0, box);
                    result.Add(new Sign(prop, TracksideAtlas.Brake(metres), c.Turn, StraightModeZone.Wrap(s, site.LapLength), side,
                        metres.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    taken.Add(box);
                }
            }
            return result;
        }

        public List<Prop> Cameras(List<Obb> taken)
        {
            var result = new List<Prop>();
            foreach (var turn in CameraTurns)
            {
                var span = site.Track.Course.Corners.First(s => s.Corner.Turn == turn);
                var side = span.Corner.OutsideSide;
                foreach (var shift in Shifts(40, 5))
                {
                    var k = Row(span.Apex + shift);
                    var t = _q[k];
                    var lateral = _b.Fence(k, side) + 1.2 + CameraHalf;
                    var at = t.Position + side * t.Left * (float)lateral;
                    var apex = TrackMeshBuilder.TrackPoint(_q, span.Apex, 0);
                    var outward = Vector2.Normalize(at - new Vector2(apex.X, apex.Z));
                    var axis = new Vector2(outward.Y, -outward.X);
                    var box = new Obb(at, axis, CameraHalf, CameraHalf);
                    if (!Clear(box, 1.0) || !OffBarriers(box, taken)) continue;
                    result.Add(new Prop(0, new Vector3(at.X, Ground(box), at.Y), Yaw(axis), 1, turn, box));
                    taken.Add(box);
                    break;
                }
            }
            return result;
        }

        public Prop Screen(List<Obb> taken)
        {
            foreach (var shift in Shifts(40, 4))
                foreach (var extra in new[] { 4.0, 8.0, 12.0 })
                {
                    var s = site.LapLength - ScreenFromLapEnd + shift;
                    var k = Row(s);
                    var t = _q[k];
                    var lateral = _b.Fence(k, -1) + extra;
                    var at = t.Position - t.Left * (float)lateral;
                    // Looking back up the straight towards the grandstand and the grid.
                    var target = TrackMeshBuilder.TrackPoint(_q, s - 160, 0);
                    var outward = Vector2.Normalize(at - new Vector2(target.X, target.Z));
                    var axis = new Vector2(outward.Y, -outward.X);
                    var box = new Obb(at + outward * (ScreenDeep / 2), axis, ScreenHalf, ScreenDeep / 2 + 0.2f);
                    if (!Clear(box, 1.0) || !OffBarriers(box, taken)) continue;
                    return new Prop(0, new Vector3(at.X, Ground(box), at.Y), Yaw(axis), 1, 0, box);
                }
            throw new InvalidOperationException("No room for the big screen at the main grandstand.");
        }

        /// <summary>0, then ±step, ±2 step … up to ±max.</summary>
        private static IEnumerable<double> Shifts(double max, double step)
        {
            yield return 0;
            for (var d = step; d <= max + 1e-9; d += step)
            {
                yield return d;
                yield return -d;
            }
        }

        private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
    }
}
