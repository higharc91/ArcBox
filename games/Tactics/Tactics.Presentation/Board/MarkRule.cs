using System.Collections.Immutable;
using System.Numerics;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Board;

/// <summary>
/// Which of the units' marks the TV draws at a moment: their HP bars, Downed counts, condition icons and turn arrow. Pure: <c>TacticsScene</c>
/// applies it to what <see cref="UnitPlan.Build"/> and <see cref="BeatPlayer.Frame"/> give.
/// </summary>
public static class MarkRule
{
    private const float Gap = 2;     // above and below a count: closer than that, a number over it reads as part of it

    /// <summary>
    /// The units with no Downed count while a number pops up over that unit, or close enough beside its count to be read with it: "-10" next to
    /// a "2" reads as "-102". The count is back as soon as the number has gone.
    /// </summary>
    public static ImmutableArray<BoardUnit> ClearOfPops(ImmutableArray<BoardUnit> units, ImmutableArray<PopBox> pops, UnitArt art)
    {
        if (pops.IsDefaultOrEmpty || !units.Any(u => u.Countdown is not null))
            return units;
        return [.. units.Select(u => u.Countdown is { } count && Crowded(u, count, pops, art) ? u with { Countdown = null } : u)];
    }

    /// <summary>Where a count is drawn (<c>UnitMarks.DrawDigits</c>), in art pixels: its top left and size.</summary>
    public static (Vector2 TopLeft, Vector2 Size) CountBox(UnitCountdown count, UnitArt art)
    {
        var glyphs = count.Text.Select(art.Digit).ToArray();
        const int scale = UnitPlan.CountdownScale;
        var left = count.Centre.X - glyphs.Sum(g => g.Advance) * scale / 2f;
        var (pen, right, height) = (left, left, 0f);
        foreach (var (rect, advance) in glyphs)
        {
            (right, height) = (MathF.Max(right, pen + rect.Width * scale), MathF.Max(height, rect.Height * scale));
            pen += advance * scale;
        }
        return (new Vector2(left, count.Centre.Y - height / 2), new Vector2(right - left, height));
    }

    private static bool Crowded(BoardUnit unit, UnitCountdown count, ImmutableArray<PopBox> pops, UnitArt art)
    {
        var (topLeft, size) = CountBox(count, art);
        var reach = new Vector2(art.Digit('0').Advance * UnitPlan.CountdownScale, Gap);     // a digit's width aside: closer than that, it's one number
        var (low, high) = (topLeft - reach, topLeft + size + reach);
        return pops.Any(p => p.Unit == unit.Id
            || (p.TopLeft.X < high.X && p.TopLeft.X + p.Size.X > low.X && p.TopLeft.Y < high.Y && p.TopLeft.Y + p.Size.Y > low.Y));
    }
}
