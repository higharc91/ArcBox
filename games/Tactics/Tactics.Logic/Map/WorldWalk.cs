namespace Tactics.Logic;

/// <summary>A direction on screen, as the phone's pad sends it. Screen north is tile (-1, -1); see <see cref="WorldWalk.Delta"/>.</summary>
public enum WalkDir
{
    /// <summary>Up the screen.</summary>
    N,
    /// <summary>Up and right.</summary>
    NE,
    /// <summary>Right.</summary>
    E,
    /// <summary>Down and right.</summary>
    SE,
    /// <summary>Down the screen.</summary>
    S,
    /// <summary>Down and left.</summary>
    SW,
    /// <summary>Left.</summary>
    W,
    /// <summary>Up and left.</summary>
    NW,
}

/// <summary>
/// One step of the party. <see cref="Cost"/> is in Move, as <see cref="Paths.StepCost"/> counts it; a diagonal's is its cheaper legal
/// L-path's total ÷ 2, rounded up, since a diagonal can climb more than one straight step can. <see cref="Diagonal"/> is a step on both
/// axes, and <see cref="Slid"/> a straight step taken because the step asked for was blocked: a diagonal's axis, or the side of a jag.
/// </summary>
public readonly record struct WalkMove(Tile To, int Cost, bool Diagonal, bool Slid);

/// <summary>
/// The rule for walking the world, and the only one: the party's steps, the region checks and the world tools all use it. It adds to
/// <see cref="Paths.StepCost"/> only what a battle doesn't have: diagonals and sliding along walls. A diagonal is legal only if
/// both corner tiles are walkable and one L-path is legal leg by leg, so what can be reached with diagonals is what can be reached without.
/// </summary>
public static class WorldWalk
{
    /// <summary>The steps along one axis, in the order the floods try them.</summary>
    private static readonly WalkDir[] Straight = [WalkDir.NE, WalkDir.SE, WalkDir.SW, WalkDir.NW];

    /// <summary>The tile change a screen direction makes.</summary>
    public static (int Dx, int Dy) Delta(WalkDir dir) => dir switch
    {
        WalkDir.N => (-1, -1),
        WalkDir.NE => (0, -1),
        WalkDir.E => (1, -1),
        WalkDir.SE => (1, 0),
        WalkDir.S => (1, 1),
        WalkDir.SW => (0, 1),
        WalkDir.W => (-1, 1),
        WalkDir.NW => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, "Not a direction."),
    };

    /// <summary>
    /// The step from <paramref name="from"/>, which must be on the map, or null if the party stays put. A blocked diagonal tries its two
    /// axes, <paramref name="lastAlongX"/> saying which the party last moved along, and X first if it hasn't. A blocked straight step,
    /// which is a road direction on screen, slides round a one-tile jag (see <see cref="Jag"/>).
    /// </summary>
    public static WalkMove? Next(Terrain terrain, Tile from, WalkDir dir, int jump, bool? lastAlongX)
    {
        var (dx, dy) = Delta(dir);
        var to = new Tile(from.X + dx, from.Y + dy);
        if (dx == 0 || dy == 0)
            return Legal(terrain, from, to, jump) ? new WalkMove(to, Paths.StepCost(terrain, from, to, jump), false, false) : Jag(terrain, from, dx, dy, jump);

        var (alongX, alongY) = (new Tile(to.X, from.Y), new Tile(from.X, to.Y));
        if (DiagonalCost(terrain, from, to, alongX, alongY, jump) is { } cost)
            return new WalkMove(to, cost, true, false);

        foreach (var side in lastAlongX == false ? (ReadOnlySpan<Tile>)[alongY, alongX] : [alongX, alongY])
            if (Legal(terrain, from, side, jump))
                return new WalkMove(side, Paths.StepCost(terrain, from, side, jump), false, true);
        return null;
    }

    /// <summary>
    /// The side step round a one-tile jag, for a straight step of (<paramref name="dx"/>, <paramref name="dy"/>) that's blocked. Only
    /// when exactly one side is open, so it never has to guess what the thumb meant, and only when the step on from that side is open too:
    /// along a straight wall that step is blocked as well, so the party stands rather than slides back and forth.
    /// </summary>
    private static WalkMove? Jag(Terrain terrain, Tile from, int dx, int dy, int jump)
    {
        var (left, right) = (new Tile(from.X + dy, from.Y + dx), new Tile(from.X - dy, from.Y - dx));
        var (leftOpen, rightOpen) = (Legal(terrain, from, left, jump), Legal(terrain, from, right, jump));
        if (leftOpen == rightOpen)
            return null;
        var side = leftOpen ? left : right;
        return Legal(terrain, side, new Tile(side.X + dx, side.Y + dy), jump)
            ? new WalkMove(side, Paths.StepCost(terrain, from, side, jump), false, true)
            : null;
    }

    /// <summary>Whether one straight step is legal.</summary>
    public static bool Legal(Terrain terrain, Tile from, Tile to, int jump) => Paths.StepCost(terrain, from, to, jump) != int.MaxValue;

    /// <summary>The tiles one legal straight step from <paramref name="from"/>.</summary>
    public static IEnumerable<Tile> Steps(Terrain terrain, Tile from, int jump)
        => Straight.Select(Delta).Select(d => new Tile(from.X + d.Dx, from.Y + d.Dy)).Where(next => Legal(terrain, from, next, jump));

    /// <summary>Everything that can be walked to from <paramref name="start"/>, never entering <paramref name="closed"/>.</summary>
    public static HashSet<Tile> Reach(Terrain terrain, Tile start, int jump, IReadOnlySet<Tile>? closed = null)
    {
        var reached = new HashSet<Tile> { start };
        var open = new Stack<Tile>([start]);
        while (open.TryPop(out var tile))
            foreach (var next in Steps(terrain, tile, jump))
                if (closed?.Contains(next) != true && reached.Add(next))
                    open.Push(next);
        return reached;
    }

    /// <summary>Everything that can walk to <paramref name="end"/>: a flood along the steps backward.</summary>
    public static HashSet<Tile> ReachBack(Terrain terrain, Tile end, int jump)
    {
        var reached = new HashSet<Tile> { end };
        var open = new Stack<Tile>([end]);
        while (open.TryPop(out var tile))
            foreach (var (dx, dy) in Straight.Select(Delta))
            {
                var from = new Tile(tile.X + dx, tile.Y + dy);
                if (terrain.Walkable(from) && Legal(terrain, from, tile, jump) && reached.Add(from))
                    open.Push(from);
            }
        return reached;
    }

    /// <summary>
    /// The tiles that can be walked to from <paramref name="start"/> but can't walk back to it: the floors of one-way drops. Ground nobody
    /// can reach isn't trapped.
    /// </summary>
    public static IReadOnlySet<Tile> Trapped(Terrain terrain, Tile start, int jump)
    {
        var reached = Reach(terrain, start, jump);
        reached.ExceptWith(ReachBack(terrain, start, jump));
        return reached;
    }

    /// <summary>A diagonal's cost, or null if it isn't legal.</summary>
    private static int? DiagonalCost(Terrain terrain, Tile from, Tile to, Tile cornerX, Tile cornerY, int jump)
    {
        if (!terrain.Walkable(cornerX) || !terrain.Walkable(cornerY))
            return null;

        var cheapest = int.MaxValue;
        foreach (var corner in (ReadOnlySpan<Tile>)[cornerX, cornerY])
            if (Legal(terrain, from, corner, jump) && Legal(terrain, corner, to, jump))
                cheapest = Math.Min(cheapest, Paths.StepCost(terrain, from, corner, jump) + Paths.StepCost(terrain, corner, to, jump));
        return cheapest == int.MaxValue ? null : (cheapest + 1) / 2;
    }
}
