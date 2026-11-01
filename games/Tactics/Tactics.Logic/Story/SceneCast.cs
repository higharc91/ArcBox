using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Who stands in the world while a scene plays, and where (the user's answer of 2026-11-01, item 21): every speaker of the scene's lines
/// who isn't the narrator, at the scene's spot, until it ends. Pure dressing, as a beat's marker is: nobody here blocks the party. The
/// server decides the tiles so every TV, and a TV that comes back, shows the same thing.
/// </summary>
public static class SceneCast
{
    /// <summary>How many legal steps from the scene's spot the cast may stand, at most.</summary>
    public const int Reach = 4;

    /// <summary>How near the leader a tile behind them is, in tiles, for it to be where the followers gather (<see cref="Place"/>).</summary>
    public const float FollowerReach = 3.2f;

    /// <summary>
    /// The scene's speakers who aren't the narrator, each once, in the order they first speak: node by node as the scene lists them, line
    /// by line. Every node counts, played or not, so the cast doesn't change under a scene as its branches go.
    /// </summary>
    public static ImmutableArray<string> Speakers(SceneDef scene)
        => [.. scene.Nodes.SelectMany(n => n.Lines).Select(l => l.Speaker).Where(s => s != SceneDef.Narrator).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Where <paramref name="count"/> figures stand for a scene at <paramref name="spot"/>, in the cast's order, one to a tile: the tiles
    /// the party could walk to within <see cref="Reach"/> steps of the spot (by <see cref="WorldWalk.Next"/>, the spot itself first), that
    /// pass <paramref name="allowed"/>, and aren't in <paramref name="party"/> (the leader's tile and the trail behind it). The first speaker
    /// gets the best: clear of where the followers gather behind the leader (within <see cref="FollowerReach"/> of <paramref name="leader"/>,
    /// on the side away from <paramref name="facing"/>) if it can be, then the fewest steps from the spot, then the nearest it as the crow
    /// flies, then the lowest tile. Fewer than <paramref name="count"/> when the ground runs out: the rest of the cast isn't shown.
    /// </summary>
    public static ImmutableArray<Tile> Place(Terrain terrain, Tile spot, Tile leader, Tile facing, IReadOnlyCollection<Tile> party, int count,
        Func<Tile, bool>? allowed = null)
    {
        if (count <= 0)
            return [];
        bool Ok(Tile t) => terrain.Walkable(t) && (allowed is null || allowed(t)) && t != leader && !party.Contains(t);

        // The ground round the spot, by steps along it, the spot first.
        var steps = new Dictionary<Tile, int> { [spot] = 0 };
        var frontier = new List<Tile> { spot };
        for (var step = 1; step <= Reach && frontier.Count > 0; step++)
        {
            var next = new List<Tile>();
            foreach (var from in frontier)
            {
                if (!terrain.Walkable(from))
                    continue;
                foreach (var dir in Enum.GetValues<WalkDir>())
                    if (WorldWalk.Next(terrain, from, dir, ClassDef.DefaultJump, null) is { } move && steps.TryAdd(move.To, step))
                        next.Add(move.To);
            }
            frontier = next;
        }

        var (wx, wy) = (facing.X - leader.X, facing.Y - leader.Y);
        bool Behind(Tile t)
        {
            var (dx, dy) = (t.X - leader.X, t.Y - leader.Y);
            return dx * dx + dy * dy <= FollowerReach * FollowerReach && dx * wx + dy * wy <= 0;
        }

        return [.. steps.Where(s => Ok(s.Key))
            .OrderBy(s => Behind(s.Key) ? 1 : 0)
            .ThenBy(s => s.Value)
            .ThenBy(s => (s.Key.X - spot.X) * (s.Key.X - spot.X) + (s.Key.Y - spot.Y) * (s.Key.Y - spot.Y))
            .ThenBy(s => s.Key)
            .Take(count)
            .Select(s => s.Key)];
    }
}
