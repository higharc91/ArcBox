namespace Redline.Presentation.Course;

/// <summary>
/// A run of kerb on one side of the track: metres along the lap (<paramref name="From"/> may be negative), side (+1 left),
/// profile, and the turn it was laid for (the first, where runs of two corners merged).
/// </summary>
public readonly record struct KerbRun(int Turn, double From, double To, int Side, KerbKind Kind);

/// <summary>A yellow sausage kerb behind an apex: metres along the lap and side (+1 left).</summary>
public readonly record struct SausageRun(int Turn, double From, double To, int Side);

/// <summary>
/// Where the kerbs go and their cross-sections. Each corner gets an apex kerb on its inside from just before its entry to
/// just after its exit (its own <see cref="Corner.Kerb"/> profile; a sausage corner has a sawtooth there) and an outside kerb
/// from its entry to <see cref="ExitTrail"/> metres past the exit (flat, or sawtooth for raised-kerb corners). Runs on the same side closer than <see cref="MergeGap"/> merge into one, keeping the raised
/// profile. Sausage corners add a <see cref="SausageLength"/> sausage kerb centred on the apex, <see cref="SausageOffset"/>
/// beyond the asphalt edge. Kerbs start at the asphalt edge and are <see cref="Width"/> wide; red and white alternate
/// every <see cref="StripeLength"/> metres measured along the kerb itself.
/// </summary>
public static class Kerbs
{
    public const double Width = 1.5;
    public const double StripeLength = 1.0;
    public const double EntryLead = 4, ExitTrail = 30, MergeGap = 4;
    public const double SausageOffset = 2.1, SausageWidth = 0.45, SausageLength = 26;
    /// <summary>Sausage kerbs come in modules this long with <see cref="SausageGap"/> between them.</summary>
    public const double SausageModule = 2.0, SausageGap = 0.3;

    /// <summary>Width of the sealed joint at a kerb's base, just inside the asphalt edge (between it and the edge line).</summary>
    public const double SeamWidth = 0.045;

    /// <summary>
    /// Cross-section of a kerb from the asphalt edge outwards: (metres beyond the edge, height above the track). It starts with
    /// the dark sealed joint (<see cref="SeamWidth"/>, inside the edge, in the gap the white edge line leaves); a flat kerb is
    /// then painted just proud of the asphalt; a sawtooth kerb rises on a bevel to 5.5 cm (its ridges are in the material);
    /// both drop on an outer bevel to the verge.
    /// </summary>
    public static IReadOnlyList<(double Offset, double Height)> Profile(KerbKind kind) => kind == KerbKind.Flat
        ? [(-SeamWidth, 0.006), (0, 0.012), (0.06, 0.022), (Width - 0.06, 0.022), (Width, -0.03)]
        : [(-SeamWidth, 0.004), (0, 0.006), (0.18, 0.055), (Width - 0.15, 0.055), (Width, -0.03)];

    /// <summary>Cross-section of a sausage kerb from its inner edge (<see cref="SausageOffset"/> beyond the asphalt edge).</summary>
    public static IReadOnlyList<(double Offset, double Height)> SausageProfile { get; } =
        [(0, -0.03), (0.05, 0.09), (0.13, 0.15), (0.32, 0.15), (0.40, 0.09), (SausageWidth, -0.03)];

    public static IReadOnlyList<KerbRun> Runs(IReadOnlyList<CornerSpan> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        var raw = new List<KerbRun>();
        foreach (var span in corners)
        {
            var c = span.Corner;
            var inside = c.Kerb == KerbKind.Flat ? KerbKind.Flat : KerbKind.Sawtooth;
            raw.Add(new KerbRun(c.Turn, span.Entry - EntryLead, span.Exit + EntryLead, c.ApexSide, inside));
            raw.Add(new KerbRun(c.Turn, span.Entry - EntryLead, span.Exit + ExitTrail, c.OutsideSide, inside));
        }
        var merged = new List<KerbRun>();
        foreach (var side in new[] { +1, -1 })
        {
            KerbRun? open = null;
            foreach (var run in raw.Where(r => r.Side == side).OrderBy(r => r.From))
            {
                if (open is { } o && run.From <= o.To + MergeGap)
                {
                    open = o with { To = Math.Max(o.To, run.To), Kind = o.Kind == KerbKind.Sawtooth || run.Kind == KerbKind.Sawtooth ? KerbKind.Sawtooth : KerbKind.Flat };
                    continue;
                }
                if (open is { } done) merged.Add(done);
                open = run;
            }
            if (open is { } last) merged.Add(last);
        }
        return [.. merged.OrderBy(r => r.From)];
    }

    public static IReadOnlyList<SausageRun> Sausages(IReadOnlyList<CornerSpan> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        return [.. corners.Where(s => s.Corner.Kerb == KerbKind.Sausage)
            .Select(s => new SausageRun(s.Corner.Turn, s.Apex - SausageLength / 2, s.Apex + SausageLength / 2, s.Corner.ApexSide))];
    }
}
