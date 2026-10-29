namespace Redline.Presentation.Course;

/// <summary>
/// Everything the course dressing is placed from, computed once per circuit from its corner data and track samples: the
/// corners' extents, the racing line and its wear, the run-off profile, kerb runs, sausage kerbs, grid boxes and painted
/// marks. Later placement (barriers, marshal posts, brake boards) reads <see cref="Corners"/> and <see cref="Runoff"/>.
/// </summary>
public sealed record CourseLayout(
    IReadOnlyList<CornerSpan> Corners,
    RacingLine RacingLine,
    RunoffProfile Runoff,
    IReadOnlyList<KerbRun> Kerbs,
    IReadOnlyList<SausageRun> Sausages,
    IReadOnlyList<GridBox> Grid,
    IReadOnlyList<Mark> Marks)
{
    public static CourseLayout Build(CircuitData c, IReadOnlyList<TrackSample> q)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(q);
        var lap = (q[1].S - q[0].S) * q.Count;
        var spans = CourseCurvature.Spans(q, c.Corners, lap);
        return new CourseLayout(
            spans,
            RacingLine.Build(q, spans),
            RunoffProfile.Build(q, spans),
            Course.Kerbs.Runs(spans),
            Course.Kerbs.Sausages(spans),
            Markings.Grid(),
            Markings.All(c, q));
    }
}
