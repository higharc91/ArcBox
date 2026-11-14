namespace Meridian.Logic;

/// <summary>Where a crew member can get to, and what it costs (spec §2.4).</summary>
public static class Reach
{
    /// <summary>
    /// The cost of entering a neighbour from <paramref name="from"/>: 1, plus the exit cost of the compartment being left.
    /// </summary>
    public static int StepCost(RulesPack pack, Compartment from) => 1 + pack.Tile(from.Tile).ExitCost;

    /// <summary>
    /// Every compartment reachable from <paramref name="at"/> for at most <paramref name="budget"/> movement points,
    /// with its cheapest cost, across matched hatches and links. Includes <paramref name="at"/> itself at 0.
    /// Dijkstra over at most ~50 nodes, so a sorted scan is simpler than a heap and just as fast.
    /// </summary>
    public static IReadOnlyDictionary<CompartmentId, int> From(RulesPack pack, ShipState ship, CompartmentId at, int budget)
    {
        var best = new Dictionary<CompartmentId, int> { [at] = 0 };
        var done = new HashSet<CompartmentId>();
        while (true)
        {
            CompartmentId? next = null;
            var cost = int.MaxValue;
            foreach (var (id, c) in best)
            {
                if (!done.Contains(id) && (c < cost || (c == cost && next is { } n && id.CompareTo(n) < 0)))
                {
                    (next, cost) = (id, c);
                }
            }
            if (next is not { } current)
            {
                return best;
            }
            done.Add(current);

            var here = ship.Get(current);
            var step = cost + StepCost(pack, here);
            if (step > budget)
            {
                continue;
            }
            foreach (var neighbour in ship.Adjacent(here).Select(c => c.Id).Concat(ship.Linked(current)))
            {
                if (!best.TryGetValue(neighbour, out var known) || step < known)
                {
                    best[neighbour] = step;
                }
            }
        }
    }

    /// <summary>Distances with no budget: how far everything is.</summary>
    public static IReadOnlyDictionary<CompartmentId, int> Distances(RulesPack pack, ShipState ship, CompartmentId at)
        => From(pack, ship, at, int.MaxValue / 2);

    /// <summary>
    /// Cheapest cost and the previous compartment on the cheapest path, for everything reachable with no budget. One
    /// search gives a bot its whole route, instead of a search per candidate move.
    /// </summary>
    public static IReadOnlyDictionary<CompartmentId, (int Cost, CompartmentId? Via)> Paths(RulesPack pack, ShipState ship, CompartmentId at)
    {
        var best = new Dictionary<CompartmentId, (int Cost, CompartmentId? Via)> { [at] = (0, null) };
        var done = new HashSet<CompartmentId>();
        while (true)
        {
            CompartmentId? next = null;
            var cost = int.MaxValue;
            foreach (var (id, entry) in best)
            {
                if (!done.Contains(id) && (entry.Cost < cost || (entry.Cost == cost && next is { } n && id.CompareTo(n) < 0)))
                {
                    (next, cost) = (id, entry.Cost);
                }
            }
            if (next is not { } current)
            {
                return best;
            }
            done.Add(current);
            var here = ship.Get(current);
            var step = cost + StepCost(pack, here);
            foreach (var neighbour in ship.Adjacent(here).Select(c => c.Id).Concat(ship.Linked(current)))
            {
                if (!best.TryGetValue(neighbour, out var known) || step < known.Cost)
                {
                    best[neighbour] = (step, current);
                }
            }
        }
    }
}
