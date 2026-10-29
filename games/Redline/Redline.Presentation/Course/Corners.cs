namespace Redline.Presentation.Course;

/// <summary>Kerb profile at a corner: flat painted, raised sawtooth with bevels, or sawtooth plus a yellow sausage kerb behind the apex.</summary>
public enum KerbKind { Flat, Sawtooth, Sausage }

/// <summary>What lies beyond the kerb on the outside of a corner.</summary>
public enum RunoffKind { TarmacStriped, Tarmac, Gravel, Grass }

/// <summary>The barrier type behind a corner's run-off (placed by the trackside task).</summary>
public enum BarrierKind { TecPro, Tyres, Armco, Wall }

/// <summary>How hard cars brake for a corner: drives skid marks and brake boards.</summary>
public enum Braking { None, Light, Medium, Heavy }

/// <summary>How sure the data is: the turn match and its attributes.</summary>
public enum Confidence { Low, Medium, High }

/// <summary>
/// One of the circuit's official turns (<c>corners</c> in the circuit json). <paramref name="S"/> is the apex in metres
/// along the lap; <paramref name="Direction"/> is +1 for a right turn and -1 for a left turn (as
/// <see cref="TrackSample.Turn"/>); <paramref name="ApexSide"/> is the lateral sign of the apex (+1 left of travel), which
/// for every real corner is its inside.
/// </summary>
public sealed record Corner(
    int Turn,
    double S,
    int Direction,
    int ApexSide,
    KerbKind Kerb,
    RunoffKind Runoff,
    BarrierKind Barrier,
    Braking Braking,
    Confidence Confidence)
{
    /// <summary>Lateral sign of the outside of the corner (+1 left of travel).</summary>
    public int OutsideSide => -ApexSide;
}

/// <summary>
/// A 2026 straight-mode (active aero) zone: the wing opens at <paramref name="Activation"/> and closes at
/// <paramref name="End"/>, both metres along the lap; a zone may run over the start line (End &lt; Activation).
/// </summary>
public readonly record struct StraightModeZone(double Activation, double End)
{
    public double Length(double lap) => Wrap(End - Activation, lap);

    public bool Contains(double s, double lap) => Wrap(s - Activation, lap) <= Length(lap);

    internal static double Wrap(double v, double lap)
    {
        var r = v % lap;
        return r < 0 ? r + lap : r;
    }
}

/// <summary>A corner's extent along the lap: entry, apex and exit in metres (entry may be negative, exit past the lap).</summary>
public readonly record struct CornerSpan(Corner Corner, double Entry, double Apex, double Exit);

/// <summary>A local extremum of the signed curvature: sample index, metres along the lap and curvature (1/m, + right).</summary>
public readonly record struct CurvaturePeak(int Index, double S, double Curvature);

/// <summary>
/// Signed centre-line curvature and its peaks, the evidence the corner data is matched against. Curvature is the heading
/// change between the tangents <see cref="HalfWindow"/> samples either side, divided by that arc length (1/m), so peaks
/// mark where the polyline bends hardest; positive is a right turn (as <see cref="TrackSample.Turn"/>).
/// </summary>
public static class CourseCurvature
{
    public const int HalfWindow = 4;
    /// <summary>Weakest curvature that counts as a corner peak: a 250 m radius.</summary>
    public const double PeakThreshold = 1 / 250.0;
    /// <summary>Peaks closer than this (metres) are one peak, the stronger.</summary>
    public const double PeakMerge = 24;
    /// <summary>Half-width (samples) of the box blur that <see cref="Smoothed"/> applies twice.</summary>
    public const int SpanBlur = 4;

    public static double[] Signed(IReadOnlyList<TrackSample> q)
    {
        ArgumentNullException.ThrowIfNull(q);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var raw = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = q[((i - HalfWindow) % n + n) % n].Tangent;
            var b = q[(i + HalfWindow) % n].Tangent;
            raw[i] = Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y) / (2 * HalfWindow * spacing);
        }
        // A light [1 2 3 2 1] smoothing so a polyline vertex doesn't read as two peaks.
        var k = new double[n];
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            for (var d = -2; d <= 2; d++)
                sum += (3 - Math.Abs(d)) * raw[((i + d) % n + n) % n];
            k[i] = sum / 9;
        }
        return k;
    }

    /// <summary>
    /// <see cref="Signed"/> box-blurred twice over ±<see cref="SpanBlur"/> samples: the bend as a whole rather than the
    /// polyline's vertices, for corner extents.
    /// </summary>
    public static double[] Smoothed(IReadOnlyList<TrackSample> q)
    {
        var k = Signed(q);
        for (var pass = 0; pass < 2; pass++)
        {
            var n = k.Length;
            var next = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var d = -SpanBlur; d <= SpanBlur; d++)
                    sum += k[((i + d) % n + n) % n];
                next[i] = sum / (2 * SpanBlur + 1);
            }
            k = next;
        }
        return k;
    }

    /// <summary>Local maxima of |curvature| above <see cref="PeakThreshold"/>, merged within <see cref="PeakMerge"/>, in lap order.</summary>
    public static IReadOnlyList<CurvaturePeak> Peaks(IReadOnlyList<TrackSample> q)
    {
        var k = Signed(q);
        var n = k.Length;
        var spacing = q[1].S - q[0].S;
        var merge = (int)Math.Round(PeakMerge / spacing);
        var peaks = new List<CurvaturePeak>();
        for (var i = 0; i < n; i++)
        {
            var a = Math.Abs(k[i]);
            if (a < PeakThreshold) continue;
            var best = true;
            for (var d = -merge; d <= merge && best; d++)
            {
                if (d == 0) continue;
                var j = ((i + d) % n + n) % n;
                if (Math.Sign(k[j]) == Math.Sign(k[i]) && (Math.Abs(k[j]) > a || (Math.Abs(k[j]) == a && d < 0)))
                    best = false;
            }
            if (best)
                peaks.Add(new CurvaturePeak(i, q[i].S, k[i]));
        }
        return peaks;
    }

    /// <summary>
    /// Each corner's extent: from the apex outwards while the <see cref="Smoothed"/> curvature keeps the corner's sign and stays
    /// above a quarter of its value at the apex (and above a 400 m radius), but never past halfway to the neighbouring apexes.
    /// </summary>
    public static IReadOnlyList<CornerSpan> Spans(IReadOnlyList<TrackSample> q, IReadOnlyList<Corner> corners, double lap)
    {
        ArgumentNullException.ThrowIfNull(corners);
        var k = Smoothed(q);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var spans = new List<CornerSpan>(corners.Count);
        for (var c = 0; c < corners.Count; c++)
        {
            var corner = corners[c];
            var apex = (int)Math.Round(StraightModeZone.Wrap(corner.S, lap) / spacing) % n;
            var limit = Math.Max(1 / 400.0, 0.25 * Math.Abs(k[apex]));
            var prev = corners[(c - 1 + corners.Count) % corners.Count].S;
            var next = corners[(c + 1) % corners.Count].S;
            var maxBack = StraightModeZone.Wrap(corner.S - prev, lap) / 2;
            var maxAhead = StraightModeZone.Wrap(next - corner.S, lap) / 2;
            if (corners.Count == 1) maxBack = maxAhead = lap / 2;

            bool Inside(int d) => Math.Sign(k[((apex + d) % n + n) % n]) == corner.Direction
                && Math.Abs(k[((apex + d) % n + n) % n]) >= limit;
            var back = 0;
            while ((back + 1) * spacing <= maxBack && Inside(-(back + 1))) back++;
            var ahead = 0;
            while ((ahead + 1) * spacing <= maxAhead && Inside(ahead + 1)) ahead++;
            spans.Add(new CornerSpan(corner, corner.S - back * spacing, corner.S, corner.S + ahead * spacing));
        }
        return spans;
    }
}
