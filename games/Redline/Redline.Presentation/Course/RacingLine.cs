namespace Redline.Presentation.Course;

/// <summary>
/// The rubbered racing line and the wear around it, per track sample:
/// <list type="bullet">
/// <item><see cref="Lateral"/>: the line's offset from the centre line (metres, + left), keeping <see cref="EdgeMargin"/>
/// from both asphalt edges. It is built apex to apex: each corner puts the line on its outside at turn-in, on its inside
/// at the apex and back on its outside at track-out; where two corners' outside points fall between their apexes (the
/// esses, a multi-apex bend) the outside points give way and the line runs straight from apex to apex. Control points are
/// joined with eased curves and the result is smoothed over <see cref="Smoothing"/> metres.</item>
/// <item><see cref="Rubber"/>: how dark the line is, 0..1 (light on the straights, heavy through the corners);</item>
/// <item><see cref="Skid"/>: braking-zone skid marks, 0..1, before the corners that are braked for;</item>
/// <item><see cref="Marbles"/>: rubber pellets just off line from the apex to the exit, signed by the side of the line
/// they lie on (+ left), magnitude 0..1.</item>
/// </list>
/// </summary>
public sealed class RacingLine
{
    /// <summary>The line's centre stays this far inside each asphalt edge: half the wheel track, a tyre and a soft edge.</summary>
    public const double EdgeMargin = 1.9;
    /// <summary>Half-width (metres) of the box blur applied three times to the eased control curve.</summary>
    public const double Smoothing = 10;
    /// <summary>Skid-mark length before the corner entry, by braking.</summary>
    public const double HeavyBrakeZone = 115, MediumBrakeZone = 65, LightBrakeZone = 30;

    private RacingLine(double[] lateral, double[] rubber, double[] skid, double[] marbles)
    {
        Lateral = lateral;
        Rubber = rubber;
        Skid = skid;
        Marbles = marbles;
    }

    public IReadOnlyList<double> Lateral { get; }
    public IReadOnlyList<double> Rubber { get; }
    public IReadOnlyList<double> Skid { get; }
    public IReadOnlyList<double> Marbles { get; }

    /// <summary>Where the line is pulled to: metres along the lap (unwrapped) and a lateral sign times the local bound.</summary>
    private readonly record struct Control(double S, double Side, int Corner, bool Apex);

    /// <summary>Distance before the entry where the line is fully on the outside: longer for longer corners.</summary>
    public static double TurnInLead(CornerSpan c) => Math.Clamp(0.8 * (c.Apex - c.Entry) + 15, 20, 55);

    /// <summary>Distance after the exit where the line is back on the outside.</summary>
    public static double TrackOutTrail(CornerSpan c) => Math.Clamp(0.8 * (c.Exit - c.Apex) + 12, 18, 60);

    public static RacingLine Build(IReadOnlyList<TrackSample> q, IReadOnlyList<CornerSpan> corners)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(corners);
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var lap = spacing * n;
        var bound = q.Select(t => Math.Max(0, TrackGeometry.Width(t.S) / 2 - EdgeMargin)).ToArray();

        var lateral = corners.Count == 0 ? new double[n] : Line(q, corners, bound, spacing, lap);

        // Rubber follows the centre-line curvature: light on straights, darkest where the cars lean on the tyres.
        var k = CourseCurvature.Signed(q);
        var rubber = new double[n];
        var reach = (int)Math.Round(30 / spacing);
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            for (var d = -reach; d <= reach; d++)
                sum += Math.Abs(k[((i + d) % n + n) % n]);
            var bend = sum / (2 * reach + 1);
            rubber[i] = 0.45 + 0.55 * Smooth(bend * 45);
        }

        var skid = new double[n];
        var marbles = new double[n];
        foreach (var span in corners)
        {
            var corner = span.Corner;
            var zone = corner.Braking switch
            {
                Braking.Heavy => HeavyBrakeZone,
                Braking.Medium => MediumBrakeZone,
                Braking.Light => LightBrakeZone,
                _ => 0,
            };
            var strength = corner.Braking switch { Braking.Heavy => 1.0, Braking.Medium => 0.6, _ => 0.3 };
            if (zone > 0)
            {
                // From the braking point to a third of the way into the corner; strongest in the first metres of braking.
                var turnIn = span.Entry + (span.Apex - span.Entry) * 0.35;
                var start = span.Entry - zone;
                for (var s = start; s <= turnIn; s += spacing)
                {
                    var i = Index(s, spacing, n);
                    var rise = Smooth((s - start) / 12);
                    var fade = 1 - 0.6 * Smooth((s - start) / (turnIn - start));
                    var tail = Smooth((turnIn - s) / 15);
                    skid[i] = Math.Max(skid[i], strength * rise * fade * tail);
                }
            }

            // Marbles where the bend is tight enough to scrub the tyres: just off line on the outside, apex to exit.
            var apexK = Math.Abs(k[Index(span.Apex, spacing, n)]);
            var tight = Smooth((apexK - 1 / 150.0) / (1 / 40.0 - 1 / 150.0));
            if (tight <= 0) continue;
            var from = span.Apex - 10;
            var to = span.Exit + 45;
            for (var s = from; s <= to; s += spacing)
            {
                var i = Index(s, spacing, n);
                var bump = Math.Sin(Math.PI * Math.Clamp((s - from) / (to - from), 0, 1));
                var value = corner.OutsideSide * tight * bump;
                if (Math.Abs(value) > Math.Abs(marbles[i])) marbles[i] = value;
            }
        }
        return new RacingLine(lateral, rubber, skid, marbles);
    }

    private static double[] Line(IReadOnlyList<TrackSample> q, IReadOnlyList<CornerSpan> corners, double[] bound, double spacing, double lap)
    {
        var n = q.Count;
        // Per corner: outside at turn-in, inside at the apex, outside at track-out.
        var controls = new List<Control>();
        for (var c = 0; c < corners.Count; c++)
        {
            var span = corners[c];
            var apex = span.Corner.ApexSide;
            controls.Add(new Control(span.Entry - TurnInLead(span), -apex, c, false));
            controls.Add(new Control(span.Apex, apex, c, true));
            controls.Add(new Control(span.Exit + TrackOutTrail(span), -apex, c, false));
        }

        // Between two apexes, an outside point that falls past the other corner's apex is dropped; outside points of two
        // corners that cross each other merge (same side: one point at their midpoint, halfway out) or give way to a straight
        // apex-to-apex run (opposite sides).
        var keep = new List<Control>();
        for (var c = 0; c < corners.Count; c++)
        {
            var next = (c + 1) % corners.Count;
            var apexHere = corners[c].Apex;
            var apexNext = corners[next].Apex + (next == 0 ? lap : 0);
            var exit = controls[3 * c + 2];
            var entry = controls[3 * next];
            var entryS = entry.S + (next == 0 ? lap : 0);
            keep.Add(controls[3 * c + 1]);
            if (exit.S < entryS)
            {
                keep.Add(exit);
                keep.Add(entry with { S = entryS });
            }
            else if (exit.Side == entry.Side)
            {
                var mid = (Math.Max(exit.S, apexHere + 10) + Math.Min(entryS, apexNext - 10)) / 2;
                if (mid > apexHere && mid < apexNext)
                    keep.Add(new Control(mid, exit.Side * 0.5, c, false));
            }
        }

        // Eased interpolation between consecutive controls (zero slope at each, so apexes and outsides are extremes).
        var raw = new double[n];
        for (var j = 0; j < keep.Count; j++)
        {
            var a = keep[j];
            var b = j + 1 < keep.Count ? keep[j + 1] : keep[0] with { S = keep[0].S + lap };
            for (var s = Math.Ceiling(a.S / spacing) * spacing; s < b.S; s += spacing)
            {
                var i = Index(s, spacing, n);
                var t = Smooth((s - a.S) / (b.S - a.S));
                raw[i] = (a.Side + (b.Side - a.Side) * t) * bound[i];
            }
        }

        // Three box blurs (close to a Gaussian), then the bound again.
        var radius = (int)Math.Round(Smoothing / spacing);
        for (var pass = 0; pass < 3; pass++)
        {
            var next = new double[n];
            for (var i = 0; i < n; i++)
            {
                double sum = 0;
                for (var d = -radius; d <= radius; d++)
                    sum += raw[((i + d) % n + n) % n];
                next[i] = sum / (2 * radius + 1);
            }
            raw = next;
        }
        for (var i = 0; i < n; i++)
            raw[i] = Math.Clamp(raw[i], -bound[i], bound[i]);
        return raw;
    }

    private static int Index(double s, double spacing, int n) => (((int)Math.Round(s / spacing)) % n + n) % n;

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
