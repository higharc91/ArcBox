using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Dev-only: a walk across a region by the party's own rule, for <c>--world-demo</c> to play with no server.</summary>
public static class WorldRoute
{
    /// <summary>The fewest legal steps from <paramref name="start"/> to the tile farthest from it. The same every time.</summary>
    public static ImmutableArray<Tile> Across(Terrain terrain, Tile start, int jump)
    {
        var from = new Dictionary<Tile, Tile> { [start] = start };
        var open = new Queue<Tile>([start]);
        var last = start;
        while (open.TryDequeue(out var tile))
        {
            last = tile;
            foreach (var dir in Enum.GetValues<WalkDir>())
                if (WorldWalk.Next(terrain, tile, dir, jump, null) is { } move && from.TryAdd(move.To, tile))
                    open.Enqueue(move.To);
        }

        var route = new List<Tile> { last };
        while (route[^1] != start)
            route.Add(from[route[^1]]);
        route.Reverse();
        return [.. route];
    }

    /// <summary>Where a walker is, <paramref name="steps"/> along <paramref name="route"/>: on the way between two tiles, and at the last once it's there.</summary>
    public static UnitPlace At(BoardTerrain terrain, ImmutableArray<Tile> route, float steps)
    {
        var index = (int)MathF.Floor(Math.Max(steps, 0));
        if (index >= route.Length - 1)
            return UnitPlace.On(terrain, route[^1]);
        var (from, to) = (UnitPlace.On(terrain, route[index]), UnitPlace.On(terrain, route[index + 1]));
        var part = steps - index;
        return new UnitPlace(Vector2.Lerp(from.Map, to.Map, part), from.Height + (to.Height - from.Height) * part);
    }

    /// <summary>Which way a walker <paramref name="steps"/> along is heading, on the map.</summary>
    public static Vector2 Heading(ImmutableArray<Tile> route, float steps)
    {
        var index = Math.Clamp((int)steps, 0, route.Length - 2);
        return new Vector2(route[index + 1].X - route[index].X, route[index + 1].Y - route[index].Y);
    }
}
