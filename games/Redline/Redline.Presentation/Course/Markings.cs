using System.Numerics;

namespace Redline.Presentation.Course;

/// <summary>What a painted mark is made of.</summary>
public enum Paint { White, Black, Sealant }

/// <summary>
/// One painted quad in track coordinates: each corner is (X = metres along the lap, Y = metres left of the centre line).
/// The mesh builder drapes it on the asphalt; the corners may come in either winding.
/// </summary>
public readonly record struct Mark(Vector2 A, Vector2 B, Vector2 C, Vector2 D, Paint Paint)
{
    public static Mark Box(double s0, double s1, double lat0, double lat1, Paint paint = Paint.White) => new(
        new Vector2((float)s0, (float)lat1), new Vector2((float)s1, (float)lat1),
        new Vector2((float)s1, (float)lat0), new Vector2((float)s0, (float)lat0), paint);

    /// <summary>A stroke of <paramref name="width"/> metres from p to r (track coordinates).</summary>
    public static Mark Stroke(Vector2 p, Vector2 r, double width, Paint paint = Paint.White)
    {
        var d = Vector2.Normalize(r - p);
        var n = new Vector2(-d.Y, d.X) * (float)(width / 2);
        return new Mark(p + n, r + n, r - n, p - n, paint);
    }
}

/// <summary>A starting-grid box: the car's origin (midway between the axles) at <paramref name="S"/> metres along the lap
/// (negative: behind the line) and <paramref name="Lateral"/> metres left of centre; <paramref name="Position"/> is 1-based.</summary>
public readonly record struct GridBox(int Position, double S, double Lateral);

/// <summary>
/// The painted markings besides the edge lines and the start/finish chequer:
/// <list type="bullet">
/// <item>22 grid boxes at exactly the <see cref="LapMath.Grid"/> slots behind <see cref="Formation.PoleSetback"/>: a front bar
/// just ahead of the front tyres with short side brackets, and the position number beside the box on its outer side,
/// upright for a viewer looking along the race;</item>
/// <item>pit entry and pit exit lines on the pit (left) side of the main straight, with chevron hatching between line and
/// edge;</item>
/// <item>a straight-mode activation line across the track at each zone's activation point;</item>
/// <item>repair seams: dark sealant at the ends of resurfaced patches and along paving joints.</item>
/// </list>
/// </summary>
public static class Markings
{
    public const double LineWidth = 0.2;
    /// <summary>Grid box front bar distance ahead of the car's origin (front tyres reach 2.05 m).</summary>
    public const double BoxFront = 2.3;
    public const double BoxHalfWidth = 1.3;
    public const double BoxBracket = 1.1;
    public const double NumberHeight = 1.3;
    public const double NumberGap = 0.3;
    /// <summary>Pit entry and exit lane width inside the left edge.</summary>
    public const double PitLane = 3.4;
    public const double PitLineWidth = 0.3;
    public const double PitEntryFrom = 5080, PitEntryFull = 5140, PitEntryTo = 5175;
    public const double PitExitFrom = 12, PitExitFull = 70, PitExitTo = 118;
    public const double ActivationWidth = 0.3;

    /// <summary>Resurfaced patches (from, to metres along the lap): the asphalt changes tone and a seam crosses each end.</summary>
    public static IReadOnlyList<(double From, double To)> Patches { get; } =
        [(236, 268), (2196, 2238), (3322, 3366), (3640, 3668), (4934, 4962)];

    /// <summary>Paving joints (from, to, lateral): long sealed seams between paving passes on the straights.</summary>
    public static IReadOnlyList<(double From, double To, double Lateral)> Joints { get; } =
        [(690, 790, 1.1), (1712, 1830, -0.9), (2420, 2700, 0.6), (2860, 3180, 0.6), (4560, 4690, -1.4)];

    public static IReadOnlyList<GridBox> Grid(int cars = 22) =>
        [.. Enumerable.Range(0, cars).Select(g =>
        {
            var slot = LapMath.Grid(g);
            return new GridBox(g + 1, -Formation.PoleSetback - slot.Back, slot.Lateral);
        })];

    /// <summary>Every painted mark for <paramref name="c"/>, given its track samples.</summary>
    public static IReadOnlyList<Mark> All(CircuitData c, IReadOnlyList<TrackSample> q)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(q);
        var marks = new List<Mark>();
        foreach (var box in Grid())
            GridBoxMarks(marks, box);

        var half = TrackGeometry.Width(0) / 2;
        var edge = half - 0.3; // inside the white edge line
        PitLine(marks, PitEntryFrom, PitEntryFull, PitEntryTo, edge, entry: true);
        PitLine(marks, PitExitFrom, PitExitFull, PitExitTo, edge, entry: false);

        foreach (var zone in c.StraightModeZones)
        {
            var h = TrackGeometry.Width(zone.Activation) / 2 - 0.35;
            marks.Add(Mark.Box(zone.Activation - ActivationWidth / 2, zone.Activation + ActivationWidth / 2, -h, h));
        }

        foreach (var (from, to) in Patches)
            foreach (var s in new[] { from, to })
            {
                var h = TrackGeometry.Width(s) / 2 - 0.05;
                marks.Add(Mark.Box(s - 0.06, s + 0.06, -h, h, Paint.Sealant));
            }
        foreach (var (from, to, lat) in Joints)
            marks.Add(Mark.Box(from, to, lat - 0.05, lat + 0.05, Paint.Sealant));
        return marks;
    }

    private static void GridBoxMarks(List<Mark> marks, GridBox b)
    {
        var front = b.S + BoxFront;
        marks.Add(Mark.Box(front - LineWidth, front, b.Lateral - BoxHalfWidth, b.Lateral + BoxHalfWidth));
        foreach (var side in new[] { -1, 1 })
        {
            var edge = b.Lateral + side * BoxHalfWidth;
            var inner = edge - side * LineWidth;
            marks.Add(Mark.Box(front - BoxBracket, front - LineWidth, Math.Min(edge, inner), Math.Max(edge, inner)));
        }

        // The number on the outer side of the box (towards the nearer edge), upright looking along the race:
        // glyph "up" is +s and glyph "right" is towards -lateral.
        var outward = b.Lateral >= 0 ? 1 : -1;
        var text = b.Position.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var advance = Glyphs.Advance * NumberHeight;
        var textWidth = text.Length * advance - Glyphs.Spacing * NumberHeight;
        var nearLat = b.Lateral + outward * (BoxHalfWidth + NumberGap);
        // Left of the text (in its own frame) is +lateral.
        var leftLat = outward > 0 ? nearLat + textWidth : nearLat;
        var baseS = front - BoxBracket - 0.1 - NumberHeight;
        for (var k = 0; k < text.Length; k++)
        {
            var originLat = leftLat - k * advance;
            foreach (var (p, r) in Glyphs.Strokes(text[k]))
            {
                Vector2 Map(Vector2 g) => new((float)(baseS + g.Y * NumberHeight), (float)(originLat - g.X * NumberHeight));
                marks.Add(Mark.Stroke(Map(p), Map(r), Glyphs.Weight * NumberHeight));
            }
        }
    }

    /// <summary>
    /// A pit lane line: for the entry it leaves the edge at <paramref name="from"/>, reaches <see cref="PitLane"/> inside it at
    /// <paramref name="full"/> and runs on to <paramref name="to"/>; the exit runs the other way (full lane from
    /// <paramref name="from"/> to <paramref name="full"/>, back to the edge at <paramref name="to"/>). Chevrons hatch the
    /// lane where it is full width.
    /// </summary>
    private static void PitLine(List<Mark> marks, double from, double full, double to, double edge, bool entry)
    {
        var lane = edge - PitLane;
        Vector2 P(double s, double lat) => new((float)s, (float)lat);
        if (entry)
        {
            marks.Add(Mark.Stroke(P(from, edge), P(full, lane), PitLineWidth));
            marks.Add(Mark.Box(full - 0.4, to, lane - PitLineWidth / 2, lane + PitLineWidth / 2));
            Hatch(marks, full + 2, to, lane, edge);
        }
        else
        {
            marks.Add(Mark.Box(from, full + 0.4, lane - PitLineWidth / 2, lane + PitLineWidth / 2));
            marks.Add(Mark.Stroke(P(full, lane), P(to, edge), PitLineWidth));
            Hatch(marks, from + 2, from + 30, lane, edge);
        }
    }

    private static void Hatch(List<Mark> marks, double from, double to, double lane, double edge)
    {
        var mid = (lane + edge) / 2;
        var reach = (edge - lane) / 2 - 0.35;
        for (var s = from; s + reach <= to; s += 3)
        {
            marks.Add(Mark.Stroke(new Vector2((float)s, (float)mid), new Vector2((float)(s + reach), (float)(mid + reach)), 0.35));
            marks.Add(Mark.Stroke(new Vector2((float)s, (float)mid), new Vector2((float)(s + reach), (float)(mid - reach)), 0.35));
        }
    }
}
