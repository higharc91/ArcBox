using System.Numerics;

namespace Redline.Presentation.Course;

/// <summary>
/// Routes the service roads with A* on a 5 m grid over the terrain: in the infield from the plaza's entrance to every
/// infield car park and compound, outside the track from each car park and compound to the edge of the map (the county
/// road). Cells within a road's half width (plus a margin) of anything are blocked; cells near obstacles and on slopes
/// cost more, and cells already on a road cost less, so the roads share trunks. Each path is simplified and smoothed,
/// keeping a smoothed piece only where it still clears everything.
/// </summary>
internal sealed class RoadRouter
{
    private const float Cell = 5;
    private const float Margin = 4f;
    private const float Half = Surroundings.RoadWidth / 2;

    private readonly Surroundings.World _w;
    private readonly int _cols, _rows;
    private readonly float _x0, _z0;
    private readonly bool[] _blocked;
    private readonly float[] _near;
    private readonly float[] _height;
    private readonly bool[] _road;
    private readonly HashSet<int> _free = [];

    public RoadRouter(Surroundings.World w)
    {
        _w = w;
        var g = w.Site.Ground;
        _x0 = g.X0 + Cell;
        _z0 = g.Z0 + Cell;
        _cols = (int)((g.X1 - g.X0) / Cell) - 2;
        _rows = (int)((g.Z1 - g.Z0) / Cell) - 2;
        var n = _cols * _rows;
        _blocked = new bool[n];
        _height = new float[n];
        _road = new bool[n];
        for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
            {
                var p = Centre(c, r);
                _height[(r * _cols) + c] = g.Height(p.X, p.Y);
            }
        w.Stamp(_blocked, _cols, _rows, _x0, _z0, Cell, Half + Margin);
        _near = Chamfer(_blocked, _cols, _rows);
    }

    /// <summary>
    /// Outside the track (where the plaza is too) every site gets a road out to the edge of the map, nearest the plaza
    /// first, so later roads join earlier ones; in the infield, which the track encloses, the sites are chained to each
    /// other, each to the nearest one already joined.
    /// </summary>
    public List<Road> Route(GrandPlaza plaza, IReadOnlyList<CarPark> lots, IReadOnlyList<Compound> compounds)
    {
        var roads = new List<Road>();
        var promEnd = plaza.Promenade.At(-plaza.Promenade.HalfLength, 0);
        var sites = lots.Select(l => (l.Entrance, Into: l.Area.Centre, l.Infield))
            .Concat(compounds.Select(c => (c.Entrance, Into: c.Area.Centre, c.Infield)))
            .OrderBy(e => Vector2.Distance(e.Entrance, plaza.Entrance))
            .Prepend((plaza.Entrance, Into: promEnd, Infield: _w.Infield(plaza.Entrance)))
            .ToList();
        foreach (var (entrance, into, _) in sites.Where(x => !x.Infield))
        {
            if (Path(entrance, entrance, toEdge: true) is { } path)
            {
                path.Reverse();
                roads.Add(Finish(path, entrance, into));
            }
        }
        // The infield service road: waypoints every ~220 m just inside the track's clearance, joined in a ring.
        var joined = new List<Vector2>();
        var ring = InfieldWaypoints();
        for (var k = 0; k < ring.Count; k++)
        {
            var a = ring[k];
            var b = ring[(k + 1) % ring.Count];
            if (ring.Count > 1 && Vector2.Distance(a, b) < 700 && Path(a, b, toEdge: false) is { } path)
            {
                var pts = Smooth(Simplify(path.Select(x => Centre(x.C, x.R)).ToList()));
                // A stub between two waypoints with no neighbours reads as a stray strip of asphalt: skip it.
                if (Length(pts) < 60)
                    continue;
                foreach (var (c, r) in path)
                    _road[(r * _cols) + c] = true;
                roads.Add(new Road(pts, Surroundings.RoadWidth));
                joined.AddRange(pts);
            }
        }
        foreach (var (entrance, into, _) in sites.Where(x => x.Infield))
        {
            if (joined.Count > 0 && Path(joined.MinBy(j => Vector2.Distance(j, entrance)), entrance, toEdge: false) is { } path)
                roads.Add(Finish(path, entrance, into));
            joined.Add(entrance);
        }
        return roads;
    }

    /// <summary>Points in the infield, every ~220 m along the lap, 14–40 m beyond the track's clearance, where a road fits.</summary>
    private List<Vector2> InfieldWaypoints()
    {
        var q = _w.Site.Samples;
        var result = new List<Vector2>();
        var stride = (int)(220 / TrackGeometry.Step);
        for (var i = 0; i < q.Count; i += stride)
        {
            var t = q[i];
            var side = _w.Infield(t.Position + (t.Left * 40)) ? 1 : -1;
            for (var extra = 14f; extra <= 40; extra += 6)
            {
                var p = t.Position + (t.Left * (side * ((float)Surroundings.TrackClearance(t.S) + extra)));
                if (_w.Infield(p) && CellOf(p) is { } cell && !_blocked[(cell.R * _cols) + cell.C] && _w.PointFree(p, Half + Margin))
                {
                    result.Add(p);
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>Marks the path as road, simplifies and smooths it, and runs its last point up to the lot's edge.</summary>
    private Road Finish(List<(int C, int R)> cells, Vector2 entrance, Vector2 centre)
    {
        foreach (var (c, r) in cells)
            _road[(r * _cols) + c] = true;
        var pts = cells.Select(k => Centre(k.C, k.R)).ToList();
        // The last cells wander round the (unblocked) entrance: come straight in from 12 m out.
        while (pts.Count > 2 && Vector2.Distance(pts[^2], entrance) < 12)
            pts.RemoveAt(pts.Count - 2);
        pts[^1] = entrance;
        pts = Simplify(pts);
        pts = Smooth(pts);
        // Into the lot (or the promenade): stop just inside its edge (the entrance is 6 m outside it).
        var into = Vector2.Normalize(centre - entrance);
        pts.Add(entrance + (into * 7));
        return new Road(pts, Surroundings.RoadWidth);
    }

    /// <summary>A* from <paramref name="from"/> to <paramref name="to"/> (or to any border cell); null when unreachable.</summary>
    private List<(int C, int R)>? Path(Vector2 from, Vector2 to, bool toEdge)
    {
        var start = CellOf(from);
        var goal = CellOf(to);
        if (start is null || (!toEdge && goal is null))
            return null;
        var (sc, sr) = start.Value;
        var n = _cols * _rows;
        var g = new float[n];
        Array.Fill(g, float.MaxValue);
        var parent = new int[n];
        Array.Fill(parent, -1);
        var closed = new bool[n];
        var open = new PriorityQueue<int, float>();
        var s = (sr * _cols) + sc;
        g[s] = 0;
        open.Enqueue(s, H(sc, sr));
        // The start and goal cells sit next to their lot (blocked); let the search leave and enter them.
        _free.Clear();
        var free = _free;
        free.Add(s);
        if (goal is { } gl)
            Unblock(gl, free);
        Unblock((sc, sr), free);

        while (open.TryDequeue(out var cur, out _))
        {
            if (closed[cur])
                continue;
            closed[cur] = true;
            int cc = cur % _cols, cr = cur / _cols;
            if (toEdge ? (cc <= 0 || cr <= 0 || cc >= _cols - 1 || cr >= _rows - 1) : (cc == goal!.Value.C && cr == goal.Value.R))
                return Trace(parent, cur);
            for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nc = cc + dx, nr = cr + dz;
                    if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows)
                        continue;
                    var ni = (nr * _cols) + nc;
                    if (closed[ni] || (_blocked[ni] && !free.Contains(ni)))
                        continue;
                    // No corner cutting past a blocked cell.
                    if (dx != 0 && dz != 0 && ((_blocked[(cr * _cols) + nc] && !free.Contains((cr * _cols) + nc)) || (_blocked[(nr * _cols) + cc] && !free.Contains((nr * _cols) + cc))))
                        continue;
                    var step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * Cell;
                    var slope = MathF.Abs(_height[ni] - _height[cur]) / step;
                    var cost = step * (1 + (12 * slope * slope) + (2.5f * MathF.Max(0, 1 - (_near[ni] / 25))));
                    if (_road[ni])
                        cost *= 0.35f;
                    var ng = g[cur] + cost;
                    if (ng < g[ni])
                    {
                        g[ni] = ng;
                        parent[ni] = cur;
                        open.Enqueue(ni, ng + H(nc, nr));
                    }
                }
        }
        return null;

        float H(int c, int r)
        {
            if (!toEdge)
                return 0.9f * Cell * MathF.Sqrt(((c - goal!.Value.C) * (c - goal.Value.C)) + ((r - goal.Value.R) * (r - goal.Value.R)));
            return 0.9f * Cell * Math.Min(Math.Min(c, r), Math.Min(_cols - 1 - c, _rows - 1 - r));
        }
    }

    /// <summary>Lets a search through the blocked cells within 12 m of <paramref name="cell"/> (a lot's entrance).</summary>
    private void Unblock((int C, int R) cell, HashSet<int> free)
    {
        for (var dz = -2; dz <= 2; dz++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int c = cell.C + dx, r = cell.R + dz;
                if (c >= 0 && r >= 0 && c < _cols && r < _rows)
                    free.Add((r * _cols) + c);
            }
    }

    private List<(int C, int R)> Trace(int[] parent, int end)
    {
        var path = new List<(int, int)>();
        for (var i = end; i >= 0; i = parent[i])
            path.Add((i % _cols, i / _cols));
        path.Reverse();
        return path;
    }

    /// <summary>Drops points while the straight line between the kept ones still clears everything.</summary>
    private List<Vector2> Simplify(List<Vector2> pts)
    {
        var result = new List<Vector2> { pts[0] };
        var i = 0;
        while (i < pts.Count - 1)
        {
            var j = Math.Min(pts.Count - 1, i + 1);
            for (var k = pts.Count - 1; k > i + 1; k--)
            {
                if (Vector2.Distance(pts[i], pts[k]) < 160 && Clear(pts[i], pts[k]))
                {
                    j = k;
                    break;
                }
            }
            result.Add(pts[j]);
            i = j;
        }
        return result;
    }

    /// <summary>Two rounds of Chaikin corner cutting, each corner kept sharp where the cut would touch something.</summary>
    private List<Vector2> Smooth(List<Vector2> pts)
    {
        for (var round = 0; round < 3; round++)
        {
            if (pts.Count < 3)
                return pts;
            var next = new List<Vector2> { pts[0] };
            for (var i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var c = pts[i + 1];
                var lenA = Vector2.Distance(a, b);
                var lenC = Vector2.Distance(b, c);
                var cut = MathF.Min(MathF.Min(lenA, lenC) * 0.3f, 20);
                var p = b + (Vector2.Normalize(a - b) * cut);
                var q = b + (Vector2.Normalize(c - b) * cut);
                if (_w.SegmentFree(p, q, Half + 0.5f))
                {
                    next.Add(p);
                    next.Add(q);
                }
                else
                {
                    next.Add(b);
                }
            }
            next.Add(pts[^1]);
            pts = next;
        }
        return pts;
    }

    /// <summary>True when every point of the segment, every 2 m, lies in an open cell (or an end's unblocked cells).</summary>
    private bool Clear(Vector2 a, Vector2 b)
    {
        var n = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 2));
        for (var k = 0; k <= n; k++)
        {
            if (CellOf(Vector2.Lerp(a, b, k / (float)n)) is not { } cell)
                return false;
            var i = (cell.R * _cols) + cell.C;
            if (_blocked[i] && !_free.Contains(i))
                return false;
        }
        return true;
    }

    private static float Length(List<Vector2> pts) => pts.Zip(pts.Skip(1), Vector2.Distance).Sum();

    private Vector2 Centre(int c, int r) => new(_x0 + (c * Cell), _z0 + (r * Cell));

    private (int C, int R)? CellOf(Vector2 p)
    {
        var c = (int)MathF.Round((p.X - _x0) / Cell);
        var r = (int)MathF.Round((p.Y - _z0) / Cell);
        return c < 0 || r < 0 || c >= _cols || r >= _rows ? null : (c, r);
    }

    /// <summary>Two-pass chamfer distance (metres) from every cell to the nearest blocked cell.</summary>
    private static float[] Chamfer(bool[] blocked, int cols, int rows)
    {
        var d = new float[blocked.Length];
        for (var i = 0; i < d.Length; i++)
            d[i] = blocked[i] ? 0 : 1e6f;
        const float a = Cell, b = Cell * 1.4142f;
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var i = (r * cols) + c;
                if (c > 0) d[i] = MathF.Min(d[i], d[i - 1] + a);
                if (r > 0)
                {
                    d[i] = MathF.Min(d[i], d[i - cols] + a);
                    if (c > 0) d[i] = MathF.Min(d[i], d[i - cols - 1] + b);
                    if (c < cols - 1) d[i] = MathF.Min(d[i], d[i - cols + 1] + b);
                }
            }
        for (var r = rows - 1; r >= 0; r--)
            for (var c = cols - 1; c >= 0; c--)
            {
                var i = (r * cols) + c;
                if (c < cols - 1) d[i] = MathF.Min(d[i], d[i + 1] + a);
                if (r < rows - 1)
                {
                    d[i] = MathF.Min(d[i], d[i + cols] + a);
                    if (c < cols - 1) d[i] = MathF.Min(d[i], d[i + cols + 1] + b);
                    if (c > 0) d[i] = MathF.Min(d[i], d[i + cols - 1] + b);
                }
            }
        return d;
    }
}
