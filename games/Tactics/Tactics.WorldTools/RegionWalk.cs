using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>
/// The ground a region will be walked on, for proving a region before milestone 5b's walking exists. Like 5b's <c>WorldGround</c>, it
/// is the whole region as one <see cref="Terrain"/> with stamped props standing on it, and it steps through <see cref="Paths.StepCost"/>,
/// so a climb over the default Jump is blocked and a drop of any height is free. Void is impassable.
/// </summary>
public sealed class RegionWalk
{
    /// <summary>How long a straight step takes, in seconds. Milestone 5b's rate.</summary>
    public const double StepSeconds = 0.30;

    private static readonly (int X, int Y)[] Straight = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static readonly (int X, int Y)[] Diagonal = [(1, -1), (1, 1), (-1, 1), (-1, -1)];

    private readonly Terrain _terrain;

    /// <summary>The ground of <paramref name="region"/>. <paramref name="maps"/> must hold its stamped maps.</summary>
    public RegionWalk(WorldRegion region, IReadOnlyDictionary<string, BattleMap> maps)
    {
        var (width, height) = (region.Def.Size[0], region.Def.Size[1]);
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 3;
                var tile = region.TileAt(new Tile(x, y));
                (bytes[at], bytes[at + 1], bytes[at + 2]) = tile is null
                    ? ((byte)0, (byte)0, (byte)TileFlags.Impassable)
                    : ((byte)tile.Height, (byte)tile.Material, (byte)tile.Flags);
            }

        ImmutableArray<PropPlacement> props =
        [
            .. region.Def.Stamps.SelectMany(s => maps[s.Map].Props.Select(p => p with { At = new Tile(p.At.X + s.At.X, p.At.Y + s.At.Y) })),
        ];
        var world = BattleMap.FromBytes(region.Def.Id, width, height, bytes, region.Def.Materials, props, ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);
        _terrain = new Terrain(world, ImmutableSortedDictionary<Tile, int>.Empty, []);
    }

    /// <summary>The tiles one legal straight step from <paramref name="t"/>.</summary>
    public IEnumerable<Tile> Steps(Tile t) => Straight.Select(d => new Tile(t.X + d.X, t.Y + d.Y)).Where(next => Legal(t, next));

    /// <summary>Whether a party can stand on the tile, a blocking prop counting against it.</summary>
    public bool Standable(Tile t) => _terrain.Walkable(t);

    /// <summary>Whether a single straight step is legal.</summary>
    public bool Legal(Tile from, Tile to) => Paths.StepCost(_terrain, from, to, ClassDef.DefaultJump) != int.MaxValue;

    /// <summary>Everything that can be walked to from <paramref name="start"/>, never entering <paramref name="closed"/>.</summary>
    public HashSet<Tile> From(Tile start, IEnumerable<Tile>? closed = null)
    {
        var shut = closed?.ToHashSet() ?? [];
        var reached = new HashSet<Tile> { start };
        var open = new Stack<Tile>([start]);
        while (open.TryPop(out var tile))
            foreach (var next in Steps(tile))
                if (!shut.Contains(next) && reached.Add(next))
                    open.Push(next);
        return reached;
    }

    /// <summary>Everything that can walk to <paramref name="end"/>: a flood along the steps backward.</summary>
    public HashSet<Tile> To(Tile end)
    {
        var reached = new HashSet<Tile> { end };
        var open = new Stack<Tile>([end]);
        while (open.TryPop(out var tile))
            foreach (var (x, y) in Straight)
            {
                var from = new Tile(tile.X + x, tile.Y + y);
                if (_terrain.Walkable(from) && Legal(from, tile) && reached.Add(from))
                    open.Push(from);
            }
        return reached;
    }

    /// <summary>The tiles that can be walked to from <paramref name="start"/> but can't walk back to it: the floors of one-way pits.</summary>
    public IReadOnlyCollection<Tile> Trapped(Tile start)
    {
        var back = To(start);
        return [.. From(start).Where(t => !back.Contains(t))];
    }

    /// <summary>
    /// The quickest walked route, in seconds: 5b's 8-way steps at <see cref="StepSeconds"/> a tile, ×√2 on a diagonal, which is legal only
    /// if both corner tiles can be stood on and one of the two L-paths is legal leg by leg. Rough ground and long drops cost nothing
    /// extra here, as walk-time targets count tiles. Null if it can't be walked.
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
            foreach (var (x, y) in Diagonal)
            {
                var next = new Tile(tile.X + x, tile.Y + y);
                var (cornerX, cornerY) = (new Tile(next.X, tile.Y), new Tile(tile.X, next.Y));
                if (Standable(next) && Standable(cornerX) && Standable(cornerY)
                    && ((Legal(tile, cornerX) && Legal(cornerX, next)) || (Legal(tile, cornerY) && Legal(cornerY, next))))
                    Consider(next, cost + Math.Sqrt(2));
            }
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
