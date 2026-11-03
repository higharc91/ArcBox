using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Presentation.World;

/// <summary>
/// Where the world's animals (<see cref="WorldAnimalIds"/>) may wander, and how they get there. TV-only, like all ambient life: nothing here
/// touches walking or the game. An animal keeps to its own level (a step of at most <see cref="Climb"/>, so it never runs up a cliff face),
/// to dry ground or, for a water bird, to water; it stays off props and out of the tile just behind one, where its sprite, drawn on its own
/// layer, would paint over the prop in front of it; and a farm animal placed inside a fenced pen stays in that pen.
/// </summary>
public static class FaunaRange
{
    /// <summary>The most an animal climbs or drops in one step.</summary>
    public const int Climb = 1;

    /// <summary>The props that make a pen.</summary>
    public static bool IsFence(string id) => id is "fence" or "fence_join";

    /// <summary>
    /// The tiles any animal may stand on: not void, impassable or under a prop, not within a tile of a big one (a tree, a cart, a
    /// building), and not just behind one (a prop on the tile in front along +x, +y or both, or a tall prop or building up to
    /// <see cref="TallReach"/> tiles in front), since the animal's layer can't be painted over by what stands before it.
    /// </summary>
    public static ImmutableHashSet<Tile> Free(BattleMap map, Func<string, int> propHeight)
    {
        var props = map.Props.Where(p => !WorldAnimalIds.Contains(p.Id)).ToArray();
        var taken = new HashSet<Tile>();
        var tall = new HashSet<Tile>();
        foreach (var prop in props)
        {
            taken.Add(prop.At);
            var building = prop.Id.StartsWith("building:", StringComparison.Ordinal);
            if (building || propHeight(prop.Id) >= 2)
                tall.Add(prop.At);
            if (building || Big(prop.Id))
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        taken.Add(new Tile(prop.At.X + dx, prop.At.Y + dy));
        }
        var all = props.Select(p => p.At).ToHashSet();

        var free = ImmutableHashSet.CreateBuilder<Tile>();
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var tile = new Tile(x, y);
                if ((map[tile].Flags & TileFlags.Impassable) != 0 || taken.Contains(tile) || Behind(tile, all, tall))
                    continue;
                free.Add(tile);
            }
        return free.ToImmutable();
    }

    /// <summary>How far in front a tall prop or building still hides the tile behind it.</summary>
    public const int TallReach = 2;

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

    private static bool Behind(Tile t, HashSet<Tile> props, HashSet<Tile> tall)
    {
        if (props.Contains(new Tile(t.X + 1, t.Y)) || props.Contains(new Tile(t.X, t.Y + 1)) || props.Contains(new Tile(t.X + 1, t.Y + 1)))
            return true;
        for (var dy = 0; dy <= TallReach; dy++)
            for (var dx = 0; dx <= TallReach; dx++)
                if ((dx, dy) != (0, 0) && tall.Contains(new Tile(t.X + dx, t.Y + dy)))
                    return true;
        return false;
    }

    private static bool Big(string id)
        => id.Contains("tree", StringComparison.Ordinal) || id.Contains("pine", StringComparison.Ordinal) || id.Contains("boulder", StringComparison.Ordinal)
            || id.Contains("house", StringComparison.Ordinal) || id.Contains("cart", StringComparison.Ordinal) || id.Contains("rock_cluster", StringComparison.Ordinal);

    /// <summary>The inside of the smallest fenced rectangle round <paramref name="animal"/> whose four sides are each mostly fence, if any.</summary>
    private static HashSet<Tile>? Pen(Tile animal, HashSet<Tile> fences, BattleMap map)
    {
        if (fences.Count == 0)
            return null;
        (int Left, int Top, int Right, int Bottom, int Area)? best = null;
        int Count(int fromX, int toX, int fromY, int toY)
        {
            var n = 0;
            for (var y = fromY; y <= toY; y++)
                for (var x = fromX; x <= toX; x++)
                    n += fences.Contains(new Tile(x, y)) ? 1 : 0;
            return n;
        }
        for (var left = Math.Max(0, animal.X - 18); left < animal.X; left++)
            for (var right = animal.X + 1; right <= Math.Min(map.Width - 1, animal.X + 18); right++)
                for (var top = Math.Max(0, animal.Y - 18); top < animal.Y; top++)
                    for (var bottom = animal.Y + 1; bottom <= Math.Min(map.Height - 1, animal.Y + 18); bottom++)
                    {
                        var (w, h) = (right - left - 1, bottom - top - 1);
                        if (w < 3 || h < 3 || best is { Area: var area } && w * h >= area)
                            continue;
                        var (across, along) = ((right - left + 1) * 0.62, (bottom - top + 1) * 0.62);
                        if (Count(left, right, top, top) < across || Count(left, right, bottom, bottom) < across
                            || Count(left, left, top, bottom) < along || Count(right, right, top, bottom) < along)
                            continue;
                        best = (left, top, right, bottom, w * h);
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
