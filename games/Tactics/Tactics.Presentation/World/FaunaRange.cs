using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>
/// Where the world's animals (<see cref="WorldAnimalIds"/>) may wander, and how they get there. TV-only, like all ambient life: nothing here
/// touches walking or the game. An animal keeps to its own level (a step of at most <see cref="Climb"/>, so it never runs up a cliff face),
/// to dry ground or, for a water bird, to water; it stays off props and out of arm's reach of a big one; and a farm animal placed inside a
/// fenced pen stays in that pen. It may go behind a prop: animals are drawn among the world's people, in painter's order (<see cref="WorldHerd"/>),
/// so what stands in front of one hides it.
/// </summary>
public static class FaunaRange
{
    /// <summary>The most an animal climbs or drops in one step.</summary>
    public const int Climb = 1;

    /// <summary>How far a pen's side may be from the animal in it, in tiles.</summary>
    public const int PenReach = 18;

    /// <summary>The props that make a pen.</summary>
    public static bool IsFence(string id) => id is "fence" or "fence_join";

    /// <summary>The tiles any animal may stand on: not void, impassable or under a prop, and not within a tile of a big one (a tree, a cart, a building).</summary>
    public static ImmutableHashSet<Tile> Free(BattleMap map, Func<string, int> propHeight)
    {
        var taken = new HashSet<Tile>();
        foreach (var prop in map.Props.Where(p => !WorldAnimalIds.Contains(p.Id)))
        {
            taken.Add(prop.At);
            if (prop.Id.StartsWith("building:", StringComparison.Ordinal) || Big(prop.Id))
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        taken.Add(new Tile(prop.At.X + dx, prop.At.Y + dy));
        }

        var free = ImmutableHashSet.CreateBuilder<Tile>();
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var tile = new Tile(x, y);
                if ((map[tile].Flags & TileFlags.Impassable) == 0 && !taken.Contains(tile))
                    free.Add(tile);
            }
        return free.ToImmutable();
    }

    /// <summary>
    /// Where the animal at <paramref name="home"/> may go: <paramref name="free"/> tiles of its kind of ground within
    /// <paramref name="radius"/> steps of home (or inside the pen it was placed in), that it can walk to from home. Home is always in it.
    /// </summary>
    public static ImmutableArray<Tile> Domain(BattleMap map, IReadOnlySet<Tile> free, Tile home, int radius, bool water)
    {
        var fences = map.Props.Where(p => IsFence(p.Id)).Select(p => p.At).ToHashSet();
        var pen = Pen(home, fences, map);
        bool Allowed(Tile t) => t == home || free.Contains(t) && Ground(map, t, water)
            && (pen?.Contains(t) ?? Math.Abs(t.X - home.X) + Math.Abs(t.Y - home.Y) <= radius);
        var reached = new HashSet<Tile> { home };
        var queue = new Queue<Tile>([home]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in Steps(map, at))
                if (Allowed(next) && reached.Add(next))
                    queue.Enqueue(next);
        }
        return [.. reached.Order()];
    }

    /// <summary>The steps from <paramref name="from"/> to <paramref name="to"/> through <paramref name="allowed"/>, not counting the start; empty if there's no way.</summary>
    public static ImmutableArray<Tile> Path(BattleMap map, Tile from, Tile to, IReadOnlySet<Tile> allowed)
    {
        var previous = new Dictionary<Tile, Tile> { [from] = from };
        var queue = new Queue<Tile>([from]);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            var at = queue.Dequeue();
            foreach (var next in Steps(map, at))
                if (allowed.Contains(next) && previous.TryAdd(next, at))
                    queue.Enqueue(next);
        }
        if (!previous.ContainsKey(to) || from == to)
            return [];
        var path = new List<Tile>();
        for (var at = to; at != from; at = previous[at])
            path.Add(at);
        path.Reverse();
        return [.. path];
    }

    /// <summary>The four tiles round <paramref name="at"/> an animal can step to: on the map, and within <see cref="Climb"/> of its height.</summary>
    public static IEnumerable<Tile> Steps(BattleMap map, Tile at)
    {
        foreach (var next in new Tile[] { new(at.X, at.Y - 1), new(at.X + 1, at.Y), new(at.X, at.Y + 1), new(at.X - 1, at.Y) })
            if (map.InBounds(next) && Math.Abs(map[next].Height - map[at].Height) <= Climb)
                yield return next;
    }

    private static bool Ground(BattleMap map, Tile t, bool water)
    {
        var flags = map[t].Flags;
        return water ? (flags & TileFlags.Water) != 0 : (flags & (TileFlags.Water | TileFlags.Deep)) == 0;
    }

    private static bool Big(string id)
        => id.Contains("tree", StringComparison.Ordinal) || id.Contains("pine", StringComparison.Ordinal) || id.Contains("boulder", StringComparison.Ordinal)
            || id.Contains("house", StringComparison.Ordinal) || id.Contains("cart", StringComparison.Ordinal) || id.Contains("rock_cluster", StringComparison.Ordinal);

    /// <summary>
    /// The inside of the smallest fenced rectangle round <paramref name="animal"/>, its sides within <see cref="PenReach"/>, whose four sides are each mostly
    /// fence (a gate's gap is fine), if any. How much of a side is fence comes from running counts along each row and column, so each of the many rectangles
    /// tried costs four lookups: counting each side tile by tile took most of a second for a region's few farm animals, on the main thread as the world opened.
    /// </summary>
    public static HashSet<Tile>? Pen(Tile animal, IReadOnlySet<Tile> fences, BattleMap map)
    {
        if (fences.Count == 0)
            return null;
        var (x0, x1) = (Math.Max(0, animal.X - PenReach), Math.Min(map.Width - 1, animal.X + PenReach));
        var (y0, y1) = (Math.Max(0, animal.Y - PenReach), Math.Min(map.Height - 1, animal.Y + PenReach));
        var (width, height) = (x1 - x0 + 1, y1 - y0 + 1);
        // rows[y, x]: fences in row y left of x; columns[x, y]: fences in column x above y. Both relative to (x0, y0).
        var (rows, columns) = (new int[height, width + 1], new int[width, height + 1]);
        var any = false;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var fence = fences.Contains(new Tile(x0 + x, y0 + y)) ? 1 : 0;
                any |= fence != 0;
                rows[y, x + 1] = rows[y, x] + fence;
                columns[x, y + 1] = columns[x, y] + fence;
            }
        if (!any)
            return null;
        int Across(int y, int from, int to) => rows[y - y0, to - x0 + 1] - rows[y - y0, from - x0];
        int Down(int x, int from, int to) => columns[x - x0, to - y0 + 1] - columns[x - x0, from - y0];

        (int Left, int Top, int Right, int Bottom, int Area)? best = null;
        for (var left = x0; left < animal.X; left++)
            for (var right = animal.X + 1; right <= x1; right++)
            {
                var w = right - left - 1;
                if (w < 3)
                    continue;
                var across = (right - left + 1) * 0.62;
                for (var top = y0; top < animal.Y; top++)
                {
                    if (Across(top, left, right) < across)
                        continue;
                    for (var bottom = animal.Y + 1; bottom <= y1; bottom++)
                    {
                        var h = bottom - top - 1;
                        if (h < 3)
                            continue;
                        if (best is { Area: var area } && w * h >= area)
                            break;      // taller only grows it
                        var along = (bottom - top + 1) * 0.62;
                        if (Across(bottom, left, right) < across || Down(left, top, bottom) < along || Down(right, top, bottom) < along)
                            continue;
                        best = (left, top, right, bottom, w * h);
                    }
                }
            }
        if (best is not { } pen)
            return null;
        var inside = new HashSet<Tile>();
        for (var y = pen.Top + 1; y < pen.Bottom; y++)
            for (var x = pen.Left + 1; x < pen.Right; x++)
                inside.Add(new Tile(x, y));
        return inside;
    }
}
