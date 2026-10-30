using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Who is moving: their Move and Jump, and the tiles held by allies (passable) and enemies (blocked).</summary>
public sealed record Mover(int Move, int Jump, ImmutableSortedSet<Tile> Allies, ImmutableSortedSet<Tile> Enemies);

/// <summary>The result of a shortest-path search from one origin.</summary>
public sealed class PathMap
{
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cost;
    private readonly int[] _previous;
    private readonly bool[] _canEnd;

    internal PathMap(Tile origin, int width, int height, int[] cost, int[] previous, bool[] canEnd)
    {
        Origin = origin;
        _width = width;
        _height = height;
        _cost = cost;
        _previous = previous;
        _canEnd = canEnd;

        var reachable = ImmutableArray.CreateBuilder<Tile>();
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            if (canEnd[y * width + x])
                reachable.Add(new Tile(x, y));
        Reachable = reachable.ToImmutable();
    }

    /// <summary>Where the search started.</summary>
    public Tile Origin { get; }

    /// <summary>Tiles the mover may end on (cost ≤ Move, not held by an ally), including the origin, in ordinal tile order.</summary>
    public IReadOnlyCollection<Tile> Reachable { get; }

    /// <summary>Move spent to end on the tile; <see cref="int.MaxValue"/> if the mover cannot end there. The origin costs 0.</summary>
    public int CostTo(Tile t) => IndexOf(t) is var i && i >= 0 && _canEnd[i] ? _cost[i] : int.MaxValue;

    /// <summary>The steps to the tile, excluding the origin; empty if the mover cannot end there (and for the origin itself).</summary>
    public ImmutableArray<Tile> PathTo(Tile t)
    {
        var i = IndexOf(t);
        if (i < 0 || !_canEnd[i])
            return [];

        var origin = IndexOf(Origin);
        var steps = new List<Tile>();
        while (i != origin)
        {
            steps.Add(new Tile(i / _height, i % _height));
            i = _previous[i];
        }
        steps.Reverse();
        return [.. steps];
    }

    // Tiles are indexed x * height + y, which is ordinal Tile order.
    private int IndexOf(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < _width && t.Y < _height ? t.X * _height + t.Y : -1;
}

/// <summary>Movement rules and pathfinding on a <see cref="Terrain"/>.</summary>
public static class Paths
{
    // Neighbour offsets in the fixed expansion order: North, East, South, West (Y grows southwards).
    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>
    /// Dijkstra from <paramref name="origin"/>. Allies can be walked through but not ended on; enemies block.
    /// Neighbours are expanded N, E, S, W and the queue breaks cost ties by ordinal tile, so results are deterministic.
    /// </summary>
    public static PathMap From(Terrain terrain, Tile origin, Mover mover)
    {
        var map = terrain.Map;
        if (!map.InBounds(origin))
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origin is outside the map.");

        int Index(Tile t) => t.X * map.Height + t.Y; // ordinal Tile order

        var size = map.Width * map.Height;
        var cost = new int[size];
        var previous = new int[size];
        Array.Fill(cost, int.MaxValue);
        Array.Fill(previous, -1);

        var queue = new PriorityQueue<Tile, (int Cost, int Order)>();
        cost[Index(origin)] = 0;
        queue.Enqueue(origin, (0, Index(origin)));

        while (queue.TryDequeue(out var current, out var priority))
        {
            var ci = Index(current);
            if (priority.Cost > cost[ci])
                continue; // stale entry

            foreach (var (dx, dy) in Directions)
            {
                var next = new Tile(current.X + dx, current.Y + dy);
                if (mover.Enemies.Contains(next))
                    continue;
                var step = StepCost(terrain, current, next, mover.Jump);
                if (step == int.MaxValue)
                    continue;

                var total = cost[ci] + step;
                var ni = Index(next);
                if (total > mover.Move || total >= cost[ni])
                    continue;

                cost[ni] = total;
                previous[ni] = ci;
                queue.Enqueue(next, (total, ni));
            }
        }

        var canEnd = new bool[size];
        for (var i = 0; i < size; i++)
            canEnd[i] = cost[i] != int.MaxValue;
        foreach (var ally in mover.Allies)
            if (map.InBounds(ally))
                canEnd[Index(ally)] = false;
        canEnd[Index(origin)] = true;

        return new PathMap(origin, map.Width, map.Height, cost, previous, canEnd);
    }

    /// <summary>
    /// Move spent to step onto the adjacent tile <paramref name="to"/>, or <see cref="int.MaxValue"/> if the step is illegal:
    /// out of bounds, not walkable, or a climb of more than <paramref name="jump"/> levels. Otherwise 1 (2 if difficult)
    /// plus <c>max(0, climb − 1)</c>; a drop is free. Occupancy is not known here: <see cref="From"/> handles enemies.
    /// </summary>
    public static int StepCost(Terrain terrain, Tile from, Tile to, int jump)
    {
        if (!terrain.Map.InBounds(to) || !terrain.Walkable(to))
            return int.MaxValue;

        var climb = terrain.HeightAt(to) - terrain.HeightAt(from);
        if (climb > jump)
            return int.MaxValue;

        return (terrain.Difficult(to) ? 2 : 1) + Math.Max(0, climb - 1);
    }
}
