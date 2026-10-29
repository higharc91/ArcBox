namespace Redline.Presentation.Course;

/// <summary>One corner's run-off area on its outside: metres along the lap (From may be negative), side (+1 left) and kind.</summary>
public readonly record struct RunoffArea(int Turn, double From, double To, int Side, RunoffKind Kind, double Width);

/// <summary>
/// Run-off beyond the kerbs, per sample and side. Each corner whose <see cref="Corner.Runoff"/> is not grass gets an area on its
/// outside from <see cref="Lead"/> metres before its entry to <see cref="Trail"/> metres after its exit, at full width in
/// between and easing in and out over <see cref="Taper"/> metres. Laterals are metres from the centre line; the run-off
/// surface starts at the asphalt edge (<see cref="Inner"/>, under any kerb) and reaches <see cref="Outer"/>, its width
/// counted beyond the kerb. A gravel trap keeps a <see cref="GravelVerge"/> strip of plain tarmac beyond the kerb before
/// the gravel. Where areas overlap the wider one wins.
/// </summary>
public sealed class RunoffProfile
{
    public const double Lead = 25, Trail = 45, Taper = 22;
    public const double GravelVerge = 2;

    private readonly double[][] _width;
    private readonly RunoffKind[][] _kind;
    private readonly int[][] _turn;
    private readonly double[] _half;

    private RunoffProfile(IReadOnlyList<RunoffArea> areas, double[] half, double[][] width, RunoffKind[][] kind, int[][] turn)
    {
        Areas = areas;
        _half = half;
        _width = width;
        _kind = kind;
        _turn = turn;
    }

    /// <summary>The full-width areas, one per corner with run-off, in lap order.</summary>
    public IReadOnlyList<RunoffArea> Areas { get; }

    /// <summary>Full width of each kind beyond the kerb, metres.</summary>
    public static double FullWidth(RunoffKind kind) => kind switch
    {
        RunoffKind.TarmacStriped => 14,
        RunoffKind.Tarmac => 10,
        RunoffKind.Gravel => 14,
        _ => 0,
    };

    public static RunoffProfile Build(IReadOnlyList<TrackSample> q, IReadOnlyList<CornerSpan> corners)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(corners);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var half = q.Select(t => TrackGeometry.Width(t.S) / 2).ToArray();
        var width = new[] { new double[n], new double[n] };
        var kind = new[] { new RunoffKind[n], new RunoffKind[n] };
        var turn = new[] { new int[n], new int[n] };
        foreach (var k in kind) Array.Fill(k, RunoffKind.Grass);

        var areas = new List<RunoffArea>();
        foreach (var span in corners)
        {
            var c = span.Corner;
            var full = FullWidth(c.Runoff);
            if (full <= 0) continue;
            var area = new RunoffArea(c.Turn, span.Entry - Lead, span.Exit + Trail, c.OutsideSide, c.Runoff, full);
            areas.Add(area);
            var side = SideIndex(area.Side);
            for (var s = area.From - Taper; s <= area.To + Taper; s += spacing)
            {
                var ease = Smooth((s - (area.From - Taper)) / Taper) * Smooth((area.To + Taper - s) / Taper);
                var w = full * ease;
                var i = (((int)Math.Round(s / spacing)) % n + n) % n;
                if (w > width[side][i])
                {
                    width[side][i] = w;
                    kind[side][i] = c.Runoff;
                    turn[side][i] = c.Turn;
                }
            }
        }
        return new RunoffProfile(areas, half, width, kind, turn);
    }

    /// <summary>Run-off width beyond the kerb at sample <paramref name="i"/> on <paramref name="side"/> (+1 left), 0 for grass.</summary>
    public double Width(int i, int side) => _width[SideIndex(side)][i];

    public RunoffKind Kind(int i, int side) => _width[SideIndex(side)][i] > 0 ? _kind[SideIndex(side)][i] : RunoffKind.Grass;

    /// <summary>The turn whose area covers sample <paramref name="i"/> on <paramref name="side"/>, or 0.</summary>
    public int Turn(int i, int side) => _width[SideIndex(side)][i] > 0 ? _turn[SideIndex(side)][i] : 0;

    /// <summary>Distance from the centre line to the run-off surface's inner edge: the asphalt edge.</summary>
    public double Inner(int i) => _half[i];

    /// <summary>Distance from the centre line to the run-off's outer edge (the kerb's outer edge where there is none).</summary>
    public double Outer(int i, int side) => _half[i] + Kerbs.Width + Width(i, side);

    /// <summary>Distance from the centre line to the gravel's inner edge, where the run-off is gravel.</summary>
    public double GravelInner(int i, int side) => _half[i] + Kerbs.Width + Math.Min(GravelVerge, Width(i, side));

    private static int SideIndex(int side) => side > 0 ? 0 : 1;

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
