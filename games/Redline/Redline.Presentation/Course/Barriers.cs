using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Course;

/// <summary>
/// A run of one kind of barrier on one side of the track: segments <paramref name="From"/> → <paramref name="To"/> (sample
/// rows; <paramref name="To"/> may pass the lap's last row, rows wrap), side +1 left. <paramref name="Turn"/> is the corner
/// it was laid for, 0 on a straight.
/// </summary>
public readonly record struct BarrierRun(BarrierKind Kind, int Side, int From, int To, int Turn)
{
    public int Segments => To - From;
}

/// <summary>
/// Where the barriers stand and what they are, per sample row and side (spec §2.3). The barrier line is where the old
/// advertising wall stood: <see cref="TrackMeshBuilder.WallGap"/> beyond the asphalt edge, or 1 m behind the run-off where
/// that is further (so nothing reaches into <see cref="RunoffProfile.Outer"/>), and it is dropped where it would fold inside
/// a bend (<see cref="Kept"/>). A barrier's face is on the line and it reaches <see cref="Depth"/> back from it.
/// <list type="bullet">
/// <item>Every corner's outside, from <see cref="Lead"/> metres before its entry to <see cref="Trail"/> after its exit, gets
/// its <see cref="Corner.Barrier"/>: TecPro blocks, a tyre wall or armco; a <c>wall</c> corner keeps the concrete wall.</item>
/// <item>An armco corner lines both sides with armco from its lead-in to the next corner's lead-in, and the straight-mode
/// zones away from the start line (the back straight) are armco on both sides.</item>
/// <item>Everything else is the concrete wall with REDLINE/ARCBOX boards (the main straight with its grandstand and pit
/// wall, the stadium section's wall corners).</item>
/// </list>
/// Where corners' spans overlap the stronger barrier wins (TecPro, tyres, armco, wall); runs shorter than
/// <see cref="MinRun"/> rows take their neighbour's kind, so no barrier is a stub. The catch fence stands at
/// <see cref="TrackMeshBuilder.FenceOffset"/> or 0.5 m behind the barrier's back, whichever is further (dilated so it clears
/// the deepest barrier near it), and leans over towards the track at corner outsides (<see cref="Overhang"/>).
/// </summary>
public sealed class BarrierLayout
{
    public const double Lead = 40, Trail = 60;
    public const int MinRun = 8;
    /// <summary>The catch fence keeps this far behind the barrier's back.</summary>
    public const double FenceClearance = 0.5;
    /// <summary>The fence's overhang eases in and out over this many rows at each end of a corner's outside.</summary>
    public const int OverhangEase = 6;

    private readonly double[][] _line, _fence, _overhang;
    private readonly bool[][] _kept, _fenceKept;
    private readonly BarrierKind[][] _kind;
    private readonly int[][] _turn;

    private BarrierLayout(IReadOnlyList<TrackSample> q, double[][] line, bool[][] kept, BarrierKind[][] kind, int[][] turn,
        double[][] fence, bool[][] fenceKept, double[][] overhang, IReadOnlyList<BarrierRun> runs)
    {
        Samples = q;
        _line = line;
        _kept = kept;
        _kind = kind;
        _turn = turn;
        _fence = fence;
        _fenceKept = fenceKept;
        _overhang = overhang;
        Runs = runs;
    }

    public IReadOnlyList<TrackSample> Samples { get; }

    /// <summary>Every run of kept barrier segments, by side then along the lap.</summary>
    public IReadOnlyList<BarrierRun> Runs { get; }

    /// <summary>How far back a barrier reaches from its face (metres).</summary>
    public static double Depth(BarrierKind kind) => kind switch
    {
        BarrierKind.TecPro => 0.95,
        BarrierKind.Tyres => 1.25,
        BarrierKind.Armco => 0.32,
        _ => 0.3,
    };

    private static int Strength(BarrierKind kind) => kind switch
    {
        BarrierKind.TecPro => 3,
        BarrierKind.Tyres => 2,
        BarrierKind.Armco => 1,
        _ => 0,
    };

    /// <summary>Distance from the centre line to the barrier face at row <paramref name="i"/>.</summary>
    public double Line(int i, int side) => _line[Index(side)][Wrap(i)];

    /// <summary>Whether the segment from row i to i + 1 has a barrier (false where the line would fold inside a bend).</summary>
    public bool Kept(int i, int side) => _kept[Index(side)][Wrap(i)];

    /// <summary>The barrier kind of the segment from row i to i + 1.</summary>
    public BarrierKind Kind(int i, int side) => _kind[Index(side)][Wrap(i)];

    /// <summary>The turn whose outside the segment from row i belongs to, or 0.</summary>
    public int Turn(int i, int side) => _turn[Index(side)][Wrap(i)];

    public double Fence(int i, int side) => _fence[Index(side)][Wrap(i)];

    public bool FenceKept(int i, int side) => _fenceKept[Index(side)][Wrap(i)];

    /// <summary>How far the fence leans over at row i: 0 straight up, 1 the full overhang.</summary>
    public double Overhang(int i, int side) => _overhang[Index(side)][Wrap(i)];

    /// <summary>The barrier face's foot at row i (on the verge, at the track's height).</summary>
    public Vector3 Foot(int i, int side) => TrackMeshBuilder.At(Samples[Wrap(i)], side * Line(i, side), 0);

    public static BarrierLayout Build(IReadOnlyList<TrackSample> q, CourseLayout course, IReadOnlyList<StraightModeZone> zones)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(zones);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var lap = spacing * n;
        var half = q.Select(t => TrackGeometry.Width(t.S) / 2).ToArray();
        var grid = new SampleGrid(q);
        var line = new double[2][];
        var kept = new bool[2][];
        var kind = new BarrierKind[2][];
        var turn = new int[2][];
        var fence = new double[2][];
        var fenceKept = new bool[2][];
        var overhang = new double[2][];
        int Row(double s) => (((int)Math.Round(s / spacing)) % n + n) % n;
        IEnumerable<int> Rows(double from, double to)
        {
            for (var s = from; s < to; s += spacing)
                yield return Row(s);
        }

        foreach (var side in new[] { +1, -1 })
        {
            var x = Index(side);
            line[x] = TrackMeshBuilder.WallOffsets(q, half, course.Runoff, side);
            kept[x] = TrackMeshBuilder.KeptSegments(q, grid, side, line[x]);
            kind[x] = new BarrierKind[n];
            Array.Fill(kind[x], BarrierKind.Wall);
            turn[x] = new int[n];
            var outside = new double[n];

            // Armco along the straight-mode zones away from the start line (the back straight).
            foreach (var z in zones.Where(z => !z.Contains(0, lap)))
                foreach (var i in Rows(z.Activation, z.Activation + z.Length(lap)))
                    kind[x][i] = BarrierKind.Armco;
            var spans = course.Corners;
            for (var c = 0; c < spans.Count; c++)
            {
                var span = spans[c];
                if (span.Corner.Barrier != BarrierKind.Armco) continue;
                var next = spans[(c + 1) % spans.Count];
                var end = next.Entry - Lead;
                while (end < span.Entry) end += lap;
                foreach (var i in Rows(span.Entry - Lead, end))
                    kind[x][i] = BarrierKind.Armco;
            }
            // Corner outsides, the strongest barrier winning.
            foreach (var span in spans)
            {
                var corner = span.Corner;
                if (corner.OutsideSide != side) continue;
                foreach (var i in Rows(span.Entry - Lead, span.Exit + Trail))
                {
                    outside[i] = 1;
                    if (Strength(corner.Barrier) >= Strength(kind[x][i]))
                        kind[x][i] = corner.Barrier;
                    if (turn[x][i] == 0 || kind[x][i] == corner.Barrier)
                        turn[x][i] = corner.Turn;
                }
            }
            AbsorbShortRuns(kind[x], MinRun);

            // The catch fence: behind the deepest barrier within a few rows, never nearer than FenceOffset.
            fence[x] = new double[n];
            for (var i = 0; i < n; i++)
            {
                var back = 0.0;
                for (var d = -4; d <= 4; d++)
                {
                    var j = ((i + d) % n + n) % n;
                    back = Math.Max(back, line[x][j] + Depth(kind[x][j]) + FenceClearance);
                }
                fence[x][i] = Math.Max(TrackMeshBuilder.FenceOffset, back);
            }
            fenceKept[x] = TrackMeshBuilder.KeptSegments(q, grid, side, fence[x]);
            overhang[x] = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var d = -OverhangEase; d <= OverhangEase; d++)
                    sum += outside[((i + d) % n + n) % n];
                overhang[x][i] = Smooth(sum / (2 * OverhangEase + 1) * 2 - 0.5);
            }
        }
        var layout = new BarrierLayout(q, line, kept, kind, turn, fence, fenceKept, overhang, []);
        return new BarrierLayout(q, line, kept, kind, turn, fence, fenceKept, overhang, layout.FindRuns());
    }

    private List<BarrierRun> FindRuns()
    {
        var n = Samples.Count;
        var runs = new List<BarrierRun>();
        foreach (var side in new[] { +1, -1 })
        {
            // Start at a break (a dropped segment or a change of kind), so no run straddles row 0 unless the whole side is one.
            var start = Enumerable.Range(0, n).FirstOrDefault(i => !Kept(i, side) || Kind(i, side) != Kind(i - 1, side) || !Kept(i - 1, side));
            for (var step = 0; step < n;)
            {
                var i = start + step;
                if (!Kept(i, side))
                {
                    step++;
                    continue;
                }
                var k = Kind(i, side);
                var len = 0;
                while (step + len < n && Kept(i + len, side) && Kind(i + len, side) == k) len++;
                var turn = Enumerable.Range(i, len).Select(r => Turn(r, side)).FirstOrDefault(t => t != 0);
                var from = Wrap(i);
                runs.Add(new BarrierRun(k, side, from, from + len, turn));
                step += len;
            }
        }
        return [.. runs.OrderBy(r => r.Side < 0).ThenBy(r => r.From)];
    }

    /// <summary>Runs of one kind shorter than <paramref name="min"/> rows take the kind before them.</summary>
    private static void AbsorbShortRuns(BarrierKind[] kind, int min)
    {
        var n = kind.Length;
        for (var pass = 0; pass < 3; pass++)
        {
            var start = Enumerable.Range(0, n).FirstOrDefault(i => kind[i] != kind[(i - 1 + n) % n]);
            for (var step = 0; step < n;)
            {
                var i = (start + step) % n;
                var len = 0;
                while (len < n && kind[(i + len) % n] == kind[i]) len++;
                if (len < min && len < n)
                {
                    var before = kind[(i - 1 + n) % n];
                    for (var k = 0; k < len; k++)
                        kind[(i + k) % n] = before;
                }
                step += Math.Max(1, len);
            }
        }
    }

    private int Wrap(int i)
    {
        var n = Samples.Count;
        return (i % n + n) % n;
    }

    private static int Index(int side) => side > 0 ? 0 : 1;

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
