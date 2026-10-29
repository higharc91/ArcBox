using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Course;

/// <summary>The draped surfaces of a <see cref="SurroundingsPlan"/>, one mesh (one material) each.</summary>
public sealed record SurroundSurfaces(MeshData Roads, MeshData Lots, MeshData Plaza);

/// <summary>
/// What the terrain material needs per vertex: metres to the track's centre line (mown bands), wear (1 at the edge of a
/// car park, compound, road or the plaza, fading over 10 m: dust), infield (watered: greener) and hollow (low ground
/// along the draws and creeks: greener).
/// </summary>
public readonly record struct TerrainAttribute(float Distance, float Wear, bool Infield, float Hollow);

/// <summary>
/// Builds the flat things of the surroundings as meshes draped on the rendered ground (every vertex at the ground height
/// plus a small lift, vertices close enough that the terrain's 8 m triangles never poke through): the service roads, the
/// car parks and compound pads, and the plaza's paving. Also the terrain's per-vertex attributes for its material.
/// </summary>
public static class SurroundMeshes
{
    public const float RoadLift = 0.15f, LotLift = 0.1f, PromenadeLift = 0.12f, ApronLift = 0.14f;

    /// <summary>Longest edge of a draped cell.</summary>
    public const float Step = 2.5f;

    /// <summary>Lot vertex colour red channel: the surface the lot shader draws.</summary>
    public const float GrassLot = 0, GravelPad = 0.5f, PavedLot = 1;

    private static readonly Vector4 RoadColour = MeshOps.Hex("#45464a");

    public static SurroundSurfaces Build(SurroundingsPlan plan, TerrainSampler ground)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ground);
        var roads = new MeshData();
        foreach (var r in plan.Roads)
            Ribbon(roads, ground, Resample(r.Points, Step), r.Width / 2, RoadLift, RoadColour);
        roads.ComputeNormals();

        var lots = new MeshData();
        foreach (var l in plan.CarParks)
            Grid(lots, ground, l.Area, LotLift, new Vector4(l.Surface == LotSurface.Paved ? PavedLot : GrassLot, 0, 0, 1));
        foreach (var c in plan.Compounds)
            Grid(lots, ground, c.Area, LotLift, new Vector4(GravelPad, 0, 0, 1));
        lots.ComputeNormals();

        var plaza = new MeshData();
        // Promenade: UV.x = metres from its middle line (the band pattern), UV.y = metres along.
        Grid(plaza, ground, plan.Plaza.Promenade, PromenadeLift, new Vector4(1, 0, 0, 1), centredAcross: true);
        Disc(plaza, ground, plan.Plaza.Tower, plan.Plaza.ApronRadius, ApronLift);
        plaza.ComputeNormals();
        return new SurroundSurfaces(roads, lots, plaza);
    }

    /// <summary>The polyline with extra points so no piece is longer than <paramref name="step"/>.</summary>
    public static List<Vector2> Resample(IReadOnlyList<Vector2> pts, float step)
    {
        var result = new List<Vector2> { pts[0] };
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var n = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / step));
            for (var k = 1; k <= n; k++)
                result.Add(Vector2.Lerp(a, b, k / (float)n));
        }
        return result;
    }

    /// <summary>A ribbon of half width <paramref name="half"/> along <paramref name="pts"/>: left, middle and right per point.</summary>
    private static void Ribbon(MeshData m, TerrainSampler g, List<Vector2> pts, float half, float lift, Vector4 colour)
    {
        if (pts.Count < 2)
            return;
        var rows = new List<(int L, int M, int R)>();
        float along = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(pts.Count - 1, i + 1)];
            var t = Vector2.Normalize(next - prev);
            var left = new Vector2(t.Y, -t.X);
            if (i > 0)
                along += Vector2.Distance(pts[i - 1], pts[i]);
            int V(float lat)
            {
                var p = pts[i] + (left * lat);
                return m.AddVertex(new Vector3(p.X, g.Height(p.X, p.Y) + lift, p.Y), new Vector2(along, lat), colour);
            }
            rows.Add((V(half), V(0), V(-half)));
        }
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            // Left edge at row i → row i+1 → middle → ... clockwise from above (left of travel is up when travel is east).
            // On the inside of a tight bend a triangle can fold over: leave it out (the other half covers the corner).
            Tri(rows[i].L, rows[i + 1].L, rows[i].M);
            Tri(rows[i + 1].L, rows[i + 1].M, rows[i].M);
            Tri(rows[i].M, rows[i + 1].M, rows[i].R);
            Tri(rows[i + 1].M, rows[i + 1].R, rows[i].R);
        }

        void Tri(int a, int b, int c)
        {
            var n = Vector3.Cross(m.Positions[c] - m.Positions[a], m.Positions[b] - m.Positions[a]);
            if (n.Y > 1e-4f)
                m.AddTriangle(a, b, c);
        }
    }

    /// <summary>
    /// A grid over a rectangle. UV = (metres along from its start, metres across from its start edge), or across from its
    /// middle line when <paramref name="centredAcross"/>.
    /// </summary>
    private static void Grid(MeshData m, TerrainSampler g, Obb box, float lift, Vector4 colour, bool centredAcross = false)
    {
        var nu = Math.Max(1, (int)MathF.Ceiling(2 * box.HalfLength / Step));
        var nv = Math.Max(1, (int)MathF.Ceiling(2 * box.HalfWidth / Step));
        var idx = new int[nu + 1, nv + 1];
        for (var j = 0; j <= nv; j++)
            for (var i = 0; i <= nu; i++)
            {
                var u = -box.HalfLength + (2 * box.HalfLength * i / nu);
                var v = -box.HalfWidth + (2 * box.HalfWidth * j / nv);
                var p = box.At(u, v);
                var uv = centredAcross ? new Vector2(v, u + box.HalfLength) : new Vector2(u + box.HalfLength, v + box.HalfWidth);
                idx[i, j] = m.AddVertex(new Vector3(p.X, g.Height(p.X, p.Y) + lift, p.Y), uv, colour);
            }
        for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
                // Along Axis then Across (Axis turned a quarter towards +y): clockwise seen from above.
                m.AddQuad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
    }

    /// <summary>The paved apron round the tower: rings out to <paramref name="radius"/>. UV = (radius, metres round).</summary>
    private static void Disc(MeshData m, TerrainSampler g, Vector2 centre, float radius, float lift)
    {
        var rings = Math.Max(1, (int)MathF.Ceiling(radius / Step));
        var segs = Math.Max(8, (int)MathF.Ceiling(2 * MathF.PI * radius / Step));
        var colour = new Vector4(0, 0, 0, 1);
        var hub = m.AddVertex(new Vector3(centre.X, g.Height(centre.X, centre.Y) + lift, centre.Y), Vector2.Zero, colour);
        var prev = Enumerable.Repeat(hub, segs).ToArray();
        for (var k = 1; k <= rings; k++)
        {
            var r = radius * k / rings;
            var ring = new int[segs];
            for (var j = 0; j < segs; j++)
            {
                var a = 2 * MathF.PI * j / segs;
                var p = centre + (Shapes.Dir(a) * r);
                ring[j] = m.AddVertex(new Vector3(p.X, g.Height(p.X, p.Y) + lift, p.Y), new Vector2(r, a * r), colour);
            }
            for (var j = 0; j < segs; j++)
            {
                var j2 = (j + 1) % segs;
                // The angle runs east → south, clockwise seen from above: inner j → outer j → outer j2 → inner j2 is clockwise.
                if (k == 1)
                {
                    m.AddTriangle(hub, ring[j], ring[j2]);
                    continue;
                }
                m.AddQuad(prev[j], ring[j], ring[j2], prev[j2]);
            }
            prev = ring;
        }
    }

    /// <summary>
    /// Per terrain vertex, for the terrain material (<see cref="TerrainAttribute"/>): the distance to the track's centre
    /// line (exact within <see cref="ExactReach"/>, a chamfer estimate beyond, capped at <see cref="DistanceCap"/>), the
    /// wear round car parks, compounds, roads and the plaza, whether it is in the infield, and how hollow the ground is.
    /// </summary>
    public static TerrainAttribute[] TerrainAttributes(MeshData terrain, IReadOnlyList<TrackSample> samples, SurroundingsPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(samples);
        var grid = new SampleGrid(samples);
        var boxes = new List<Obb>();
        var segs = new Dictionary<(int, int), List<(Vector2 A, Vector2 B, float Half)>>();
        if (plan is not null)
        {
            boxes.AddRange(plan.CarParks.Select(l => l.Area));
            boxes.AddRange(plan.Compounds.Select(c => c.Area));
            boxes.Add(plan.Plaza.Promenade);
            boxes.Add(new Obb(plan.Plaza.Tower, plan.Plaza.AmphitheatreAxis, plan.Plaza.ApronRadius * 0.85f, plan.Plaza.ApronRadius * 0.85f));
            boxes.Add(plan.Plaza.Amphitheatre);
            foreach (var r in plan.Roads)
                for (var i = 0; i + 1 < r.Points.Count; i++)
                {
                    var a = r.Points[i];
                    var b = r.Points[i + 1];
                    for (var x = (int)MathF.Floor(MathF.Min(a.X, b.X) / 64); x <= (int)MathF.Floor(MathF.Max(a.X, b.X) / 64); x++)
                        for (var z = (int)MathF.Floor(MathF.Min(a.Y, b.Y) / 64); z <= (int)MathF.Floor(MathF.Max(a.Y, b.Y) / 64); z++)
                        {
                            if (!segs.TryGetValue((x, z), out var list))
                                segs[(x, z)] = list = [];
                            list.Add((a, b, r.Width / 2));
                        }
                }
        }
        // Distance to the centre line: exact within ExactReach (where the mown bands are), then a chamfer distance
        // over the terrain grid out to the cap (the far field only needs "far").
        var pos = terrain.Positions;
        var cols = 1;
        while (cols < pos.Count && pos[cols].Z == pos[0].Z)
            cols++;
        var rows = pos.Count / cols;
        var cell = pos.Count > 1 ? pos[1].X - pos[0].X : 1;
        var near = new HashSet<(int, int)>();
        var reach = (int)MathF.Ceiling(ExactReach / 50) + 1;
        foreach (var t in samples)
        {
            int cx = (int)MathF.Floor(t.Position.X / 50), cz = (int)MathF.Floor(t.Position.Y / 50);
            for (var dx = -reach; dx <= reach; dx++)
                for (var dz = -reach; dz <= reach; dz++)
                    near.Add((cx + dx, cz + dz));
        }
        var dist = new float[pos.Count];
        Parallel.For(0, pos.Count, i =>
        {
            var p = pos[i];
            dist[i] = DistanceCap;
            if (near.Contains(((int)MathF.Floor(p.X / 50), (int)MathF.Floor(p.Z / 50))))
            {
                var (k, d) = grid.Nearest(p.X, p.Z, ExactReach);
                if (k >= 0)
                    dist[i] = (float)d;
            }
        });
        if (cols * rows == pos.Count)
            Chamfer(dist, cols, rows, cell);

        var infield = new bool[pos.Count];
        var hollow = new float[pos.Count];
        if (cols * rows == pos.Count)
        {
            Infield(infield, pos, cols, rows, samples);
            Hollow(hollow, pos, cols, rows);
        }

        var result = new TerrainAttribute[pos.Count];
        Parallel.For(0, result.Length, i =>
        {
            var p = pos[i];
            var q = new Vector2(p.X, p.Z);
            var edge = float.MaxValue;
            foreach (var b in boxes)
                if (Vector2.Distance(b.Centre, q) < b.Reach + 12)
                    edge = MathF.Min(edge, b.Distance(q));
            var key = ((int)MathF.Floor(q.X / 64), (int)MathF.Floor(q.Y / 64));
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                    if (segs.TryGetValue((key.Item1 + dx, key.Item2 + dz), out var list))
                        foreach (var (a, b, half) in list)
                            edge = MathF.Min(edge, MathF.Max(0, Shapes.SegmentDistance(q, a, b) - half));
            var wear = edge == float.MaxValue ? 0 : Math.Clamp(1 - (edge / 10), 0, 1);
            result[i] = new TerrainAttribute(MathF.Min(dist[i], DistanceCap), wear, infield[i], hollow[i]);
        });
        return result;
    }

    /// <summary>Marks the grid vertices inside the centre line (scanlines: each row's crossings, filled in pairs).</summary>
    private static void Infield(bool[] inside, List<Vector3> pos, int cols, int rows, IReadOnlyList<TrackSample> samples)
    {
        var x0 = pos[0].X;
        var cell = pos[1].X - pos[0].X;
        var n = samples.Count;
        Parallel.For(0, rows, r =>
        {
            var z = pos[r * cols].Z;
            var xs = new List<float>();
            for (var i = 0; i < n; i++)
            {
                var a = samples[i].Position;
                var b = samples[(i + 1) % n].Position;
                if ((a.Y > z) != (b.Y > z))
                    xs.Add(a.X + ((z - a.Y) / (b.Y - a.Y) * (b.X - a.X)));
            }
            xs.Sort();
            for (var k = 0; k + 1 < xs.Count; k += 2)
            {
                var c0 = Math.Max(0, (int)MathF.Ceiling((xs[k] - x0) / cell));
                var c1 = Math.Min(cols - 1, (int)MathF.Floor((xs[k + 1] - x0) / cell));
                for (var c = c0; c <= c1; c++)
                    inside[(r * cols) + c] = true;
            }
        });
    }

    /// <summary>How far each vertex lies below its surroundings' mean (a 200 m box), 0 (level or higher) .. 1 (4 m or more).</summary>
    private static void Hollow(float[] hollow, List<Vector3> pos, int cols, int rows)
    {
        const int radius = 12;
        var h = new float[pos.Count];
        for (var i = 0; i < h.Length; i++)
            h[i] = pos[i].Y;
        var tmp = new float[h.Length];
        var blur = new float[h.Length];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                float sum = 0;
                var cnt = 0;
                for (var k = Math.Max(0, c - radius); k <= Math.Min(cols - 1, c + radius); k++, cnt++)
                    sum += h[(r * cols) + k];
                tmp[(r * cols) + c] = sum / cnt;
            }
        for (var c = 0; c < cols; c++)
            for (var r = 0; r < rows; r++)
            {
                float sum = 0;
                var cnt = 0;
                for (var k = Math.Max(0, r - radius); k <= Math.Min(rows - 1, r + radius); k++, cnt++)
                    sum += tmp[(k * cols) + c];
                blur[(r * cols) + c] = sum / cnt;
            }
        for (var i = 0; i < h.Length; i++)
            hollow[i] = Math.Clamp((blur[i] - h[i]) / 4, 0, 1);
    }

    /// <summary>Distances are exact within this many metres of the centre line.</summary>
    public const float ExactReach = 90;

    /// <summary>The terrain attribute's distance cap.</summary>
    public const float DistanceCap = 200;

    /// <summary>Two-pass chamfer propagation over a row-major grid (only lowers values).</summary>
    private static void Chamfer(float[] d, int cols, int rows, float cell)
    {
        var a = cell;
        var b = cell * 1.4142f;
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
    }
}
