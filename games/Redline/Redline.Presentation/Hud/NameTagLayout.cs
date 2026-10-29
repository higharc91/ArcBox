namespace Redline.Presentation.Hud;

/// <summary>An axis-aligned screen rectangle (pixels, y down).</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(ScreenRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>One player car's projected anchor (a point above the car) and its tag's size.</summary>
/// <param name="Behind">The anchor is behind the camera (<c>Camera3D.IsPositionBehind</c>); its projection is meaningless.</param>
/// <param name="Distance">Metres from the camera.</param>
public readonly record struct TagInput(string Code, double X, double Y, bool Behind, double Distance, double Width, double Height);

/// <summary>Where a tag goes: <see cref="Rect"/> is the tag body; the stem runs from its bottom centre to the anchor.</summary>
public readonly record struct TagPlacement(string Code, bool Visible, ScreenRect Rect, double AnchorX, double AnchorY);

/// <summary>
/// Player name tags over the 3D view (spec §4.8): culled when behind the camera, off screen, too far away to read, or under
/// a HUD panel; nearer cars keep their natural spot and farther tags that would overlap stack upwards.
/// </summary>
public static class NameTagLayout
{
    /// <summary>Gap between the tag body and the car anchor, filled by the stem.</summary>
    public const double Stem = 14;
    /// <summary>Beyond this the car is a few pixels tall and a tag would float over nothing.</summary>
    public const double MaxDistance = 260;
    private const double StackGap = 4;

    public static IReadOnlyList<TagPlacement> Layout(
        IReadOnlyList<TagInput> tags, double viewportWidth, double viewportHeight, IReadOnlyList<ScreenRect> occluders)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(occluders);
        var screen = new ScreenRect(0, 0, viewportWidth, viewportHeight);
        var placed = new List<ScreenRect>();
        var result = new Dictionary<string, TagPlacement>(StringComparer.Ordinal);

        foreach (var t in tags.OrderBy(t => t.Distance).ThenBy(t => t.Code, StringComparer.Ordinal))
        {
            var hidden = new TagPlacement(t.Code, false, default, t.X, t.Y);
            if (t.Behind || t.Distance > MaxDistance || !double.IsFinite(t.X) || !double.IsFinite(t.Y)
                || !screen.Contains(t.X, t.Y) || occluders.Any(o => o.Contains(t.X, t.Y)))
            {
                result[t.Code] = hidden;
                continue;
            }

            var rect = new ScreenRect(t.X - t.Width / 2, t.Y - Stem - t.Height, t.Width, t.Height);
            // Stack above any nearer tag it would overlap (a few passes settle a pack of three).
            for (var pass = 0; pass < placed.Count + 1; pass++)
            {
                var hit = placed.Where(p => p.Intersects(rect)).Select(p => (ScreenRect?)p).MinBy(p => p!.Value.Y);
                if (hit is not { } h) break;
                rect = rect with { Y = h.Y - StackGap - t.Height };
            }

            var onScreen = rect.X >= 0 && rect.Y >= 0 && rect.Right <= viewportWidth && rect.Bottom <= viewportHeight;
            if (!onScreen || occluders.Any(o => o.Intersects(rect)))
            {
                result[t.Code] = hidden;
                continue;
            }
            placed.Add(rect);
            result[t.Code] = new TagPlacement(t.Code, true, rect, t.X, t.Y);
        }
        return [.. tags.Select(t => result[t.Code])];
    }
}
