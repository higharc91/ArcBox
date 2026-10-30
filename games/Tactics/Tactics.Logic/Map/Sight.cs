using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>The outcome of a line-of-sight trace: how many of the three rays were blocked, and the cover that gives.</summary>
public sealed record SightResult(int RaysBlocked, Cover Cover)
{
    /// <summary>False only when all three rays are blocked.</summary>
    public bool HasLineOfSight => RaysBlocked < 3;
}

/// <summary>Line of sight and cover, computed with integer fixed point (1 level = 1024) so results never depend on floating point.</summary>
public static class Sight
{
    /// <summary>Fixed-point units per height level.</summary>
    public const int Fixed = 1024;

    // Eye height above the attacker's tile top, and the three aim heights above the target's tile top (feet, torso, head).
    private const int EyeHeight = Fixed * 3 / 2;
    private static readonly int[] TargetHeights = [Fixed / 4, Fixed, Fixed * 7 / 4];

    // A blocking prop stands 2 levels above its tile.
    private const int PropLevels = 2;

    /// <summary>
    /// Three rays from the eye (tile top + 1.5 levels) to the target at tile top + 0.25 / 1.0 / 1.75, run in 2D from the centre
    /// of <paramref name="from"/> to the centre of <paramref name="to"/>. The columns are the tiles of the segment's supercover
    /// (both ends excluded; a segment through a corner takes both neighbours). A ray is blocked in a column when its lower
    /// height there (entry or exit) is strictly below the column top, or the column holds a blocking prop reaching strictly
    /// above it; touching exactly is grazing and does not block. <paramref name="unitTiles"/>: a standing unit on a column gives
    /// half cover against <paramref name="ranged"/> attacks. A prop with cover next to the target on the attacker's side
    /// guarantees at least that cover. The result is the best of the three. Every ray is blocked when either end, or a column
    /// between, is one of the <paramref name="obscured"/> tiles (a sight-blocking zone such as smoke).
    /// </summary>
    public static SightResult Trace(Terrain terrain, Tile from, Tile to, ImmutableSortedSet<Tile> unitTiles, bool ranged,
        ImmutableSortedSet<Tile>? obscured = null)
    {
        var eye = terrain.HeightAt(from) * Fixed + EyeHeight;
        var targetTop = terrain.HeightAt(to) * Fixed;
        var columns = Columns(from, to);

        // An obscuring zone (smoke) blocks every ray at any height, so a ray through one of its tiles, or one that starts or
        // ends in one, is blocked: no line of sight into, out of or through it.
        if (from != to && obscured is { Count: > 0 }
            && (obscured.Contains(from) || obscured.Contains(to) || columns.Any(c => obscured.Contains(c.Tile))))
            return new SightResult(TargetHeights.Length, Cover.Total);

        var blocked = 0;
        foreach (var aim in TargetHeights)
            if (RayBlocked(terrain, columns, eye, targetTop + aim))
                blocked++;

        var cover = blocked switch { 0 => Cover.None, 1 => Cover.Half, 2 => Cover.ThreeQuarters, _ => Cover.Total };

        if (ranged && columns.Any(c => unitTiles.Contains(c.Tile)))
            cover = Max(cover, Cover.Half);

        var distance = Reach.Distance(from, to);
        foreach (var (dx, dy) in Neighbours)
        {
            var near = new Tile(to.X + dx, to.Y + dy);
            if (terrain.PropAt(near) is { Cover: not Cover.None } prop && Reach.Distance(near, from) < distance)
                cover = Max(cover, prop.Cover);
        }

        return new SightResult(blocked, cover);
    }

    private static readonly (int Dx, int Dy)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static Cover Max(Cover a, Cover b) => a >= b ? a : b;

    private static bool RayBlocked(Terrain terrain, List<Column> columns, int eye, int target)
    {
        foreach (var column in columns)
        {
            var top = terrain.HeightAt(column.Tile) * Fixed;
            var propTop = terrain.PropAt(column.Tile) is { Blocks: true } ? top + PropLevels * Fixed : int.MinValue;
            var limit = Math.Max(top, propTop);

            // The ray is a straight line, so its lower height in the column is at entry or exit: below the limit at either end
            // means the minimum is below it. The column is blocked when min < top, or the prop top is above min (same test).
            if (Below(eye, target, column.Entry, limit) || Below(eye, target, column.Exit, limit))
                return true;
        }

        return false;
    }

    // Ray height at t = n/d is eye + (target - eye) * n / d; compare with the limit by cross-multiplying (d > 0).
    private static bool Below(int eye, int target, Fraction t, int limit)
        => (long)eye * t.Den + (long)(target - eye) * t.Num < (long)limit * t.Den;

    /// <summary>A tile the segment crosses, with the segment parameters (0 = centre of from, 1 = centre of to) where it enters and leaves.</summary>
    private readonly record struct Column(Tile Tile, Fraction Entry, Fraction Exit);

    /// <summary>An exact rational with a positive denominator.</summary>
    private readonly record struct Fraction(long Num, long Den)
    {
        public static Fraction Of(long num, long den) => den < 0 ? new Fraction(-num, -den) : new Fraction(num, den);

        public int CompareTo(Fraction other) => (Num * other.Den).CompareTo(other.Num * Den);
    }

    // Supercover by clipping the segment against every closed tile square in its bounding box. Coordinates are doubled so tile
    // centres are odd integers (centre of x is 2x+1) and the tile square is [2x, 2x+2]; everything stays exact in integers.
    private static List<Column> Columns(Tile from, Tile to)
    {
        var columns = new List<Column>();
        var x0 = 2 * from.X + 1;
        var y0 = 2 * from.Y + 1;
        var dx = 2 * (to.X - from.X);
        var dy = 2 * (to.Y - from.Y);

        for (var x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
        for (var y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        {
            var tile = new Tile(x, y);
            if (tile == from || tile == to)
                continue;

            var lo = new Fraction(0, 1);
            var hi = new Fraction(1, 1);
            if (!Clip(x0, dx, 2 * x, 2 * x + 2, ref lo, ref hi) || !Clip(y0, dy, 2 * y, 2 * y + 2, ref lo, ref hi))
                continue;
            if (lo.CompareTo(hi) <= 0)
                columns.Add(new Column(tile, lo, hi));
        }

        return columns;
    }

    // Narrows [lo, hi] to the parameters where start + delta * t lies within [min, max]. False when the axis can never be inside.
    private static bool Clip(int start, int delta, int min, int max, ref Fraction lo, ref Fraction hi)
    {
        if (delta == 0)
            return start >= min && start <= max;

        var a = Fraction.Of(min - start, delta);
        var b = Fraction.Of(max - start, delta);
        if (a.CompareTo(b) > 0)
            (a, b) = (b, a);
        if (a.CompareTo(lo) > 0)
            lo = a;
        if (b.CompareTo(hi) < 0)
            hi = b;
        return true;
    }
}
