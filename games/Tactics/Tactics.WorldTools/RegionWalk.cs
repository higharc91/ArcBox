using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>
/// A region's walking, measured, for proving a region is good to paint and play. The ground is <see cref="WorldGround"/> and every step is
/// <see cref="WorldWalk"/>'s, at the default Jump, so this can't disagree with the party. What it adds is how long a walk takes.
/// </summary>
public sealed class RegionWalk
{
    /// <summary>How long a straight step takes, in seconds. Milestone 5b's rate.</summary>
    public const double StepSeconds = 0.30;

    private const int Jump = ClassDef.DefaultJump;

    /// <summary>The directions that step on both axes.</summary>
    private static readonly WalkDir[] Diagonals = [WalkDir.E, WalkDir.S, WalkDir.W, WalkDir.N];

    private readonly Terrain _terrain;

    /// <summary>The ground of <paramref name="region"/>. <paramref name="maps"/> must hold its stamped maps.</summary>
    public RegionWalk(WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps) : this(region, maps, null) { }

    /// <summary>The ground of <paramref name="region"/> with prop definitions.</summary>
    public RegionWalk(WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps, IReadOnlyDictionary<string, PropArtDef>? propDefs)
        => _terrain = WorldGround.Of(region, maps, propDefs).Terrain;

    /// <summary>Walking on an existing <see cref="WorldGround"/>.</summary>
    public RegionWalk(WorldGround ground) => _terrain = ground.Terrain;

    /// <summary>The tiles one legal straight step from <paramref name="t"/>.</summary>
    public IEnumerable<Tile> Steps(Tile t) => WorldWalk.Steps(_terrain, t, Jump);

    /// <summary>Whether a party can stand on the tile, a blocking prop counting against it.</summary>
    public bool Standable(Tile t) => _terrain.Walkable(t);

    /// <summary>Whether a single straight step is legal.</summary>
    public bool Legal(Tile from, Tile to) => WorldWalk.Legal(_terrain, from, to, Jump);

    /// <summary>Everything that can be walked to from <paramref name="start"/>, never entering <paramref name="closed"/>.</summary>
    public HashSet<Tile> From(Tile start, IEnumerable<Tile>? closed = null) => WorldWalk.Reach(_terrain, start, Jump, closed?.ToHashSet());

    /// <summary>Everything that can walk to <paramref name="end"/>: a flood along the steps backward.</summary>
    public HashSet<Tile> To(Tile end) => WorldWalk.ReachBack(_terrain, end, Jump);

    /// <summary>The tiles that can be walked to from <paramref name="start"/> but can't walk back to it: the floors of one-way pits.</summary>
    public IReadOnlyCollection<Tile> Trapped(Tile start) => [.. WorldWalk.Trapped(_terrain, start, Jump)];

    /// <summary>
    /// The quickest walked route, in seconds: <see cref="WorldWalk"/>'s 8-way steps at <see cref="StepSeconds"/> a tile, ×√2 on a diagonal.
    /// Rough ground and long drops cost nothing extra here, as walk-time targets count tiles. Null if it can't be walked.
    /// </summary>
    public double? Seconds(Tile start, Func<Tile, bool> arrived)
    {
        double? found = null;
        Flow(start, arrived, (_, _) => { }, seconds => found = seconds);
        return found;
    }

    /// <summary>How long each tile that can be walked to takes from <paramref name="start"/>, as <see cref="Seconds"/> counts it.</summary>
    public Dictionary<Tile, double> Times(Tile start)
    {
        var times = new Dictionary<Tile, double>();
        Flow(start, _ => false, (tile, seconds) => times[tile] = seconds, _ => { });
        return times;
    }

    private void Flow(Tile start, Func<Tile, bool> arrived, Action<Tile, double> settled, Action<double> found)
    {
        var best = new Dictionary<Tile, double> { [start] = 0 };
        var open = new PriorityQueue<Tile, double>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var tile, out var cost))
        {
            if (cost > best[tile])
                continue;
            settled(tile, cost * StepSeconds);
            if (arrived(tile))
            {
                found(cost * StepSeconds);
                return;
            }

            foreach (var next in Steps(tile))
                Consider(next, cost + 1);
            foreach (var dir in Diagonals)
                if (WorldWalk.Next(_terrain, tile, dir, Jump, null) is { Diagonal: true } move)
                    Consider(move.To, cost + Math.Sqrt(2));
        }

        void Consider(Tile to, double cost)
        {
            if (cost >= best.GetValueOrDefault(to, double.MaxValue))
                return;
            best[to] = cost;
            open.Enqueue(to, cost);
        }
    }
}
