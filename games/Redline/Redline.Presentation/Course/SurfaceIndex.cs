using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Course;

/// <summary>
/// Heights of a draped mesh (the verge) seen from above: the highest of its triangles over a ground-plane point, exactly as
/// rendered, or null where it does not reach. Triangles are bucketed on a <see cref="Bucket"/> metre grid.
/// </summary>
public sealed class SurfaceIndex
{
    public const float Bucket = 4;

    private readonly MeshData _m;
    private readonly Dictionary<(int, int), List<int>> _cells = new();

    public SurfaceIndex(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _m = mesh;
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];
            var (x0, z0) = Key(MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Z, MathF.Min(b.Z, c.Z)));
            var (x1, z1) = Key(MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
            for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue((x, z), out var list))
                        _cells[(x, z)] = list = new List<int>();
                    list.Add(t);
                }
        }
    }

    /// <summary>The highest triangle of the mesh over (x, z), or null.</summary>
    public float? Height(Vector2 p)
    {
        if (!_cells.TryGetValue(Key(p.X, p.Y), out var list))
            return null;
        float? best = null;
        foreach (var t in list)
        {
            var a = _m.Positions[_m.Indices[t]];
            var b = _m.Positions[_m.Indices[t + 1]];
            var c = _m.Positions[_m.Indices[t + 2]];
            var v0 = new Vector2(b.X - a.X, b.Z - a.Z);
            var v1 = new Vector2(c.X - a.X, c.Z - a.Z);
            var v2 = new Vector2(p.X - a.X, p.Y - a.Z);
            var den = (v0.X * v1.Y) - (v1.X * v0.Y);
            if (MathF.Abs(den) < 1e-9f) continue;
            var u = ((v2.X * v1.Y) - (v1.X * v2.Y)) / den;
            var v = ((v0.X * v2.Y) - (v2.X * v0.Y)) / den;
            if (u < -1e-4f || v < -1e-4f || u + v > 1 + 1e-4f) continue;
            var y = a.Y + (u * (b.Y - a.Y)) + (v * (c.Y - a.Y));
            if (best is null || y > best) best = y;
        }
        return best;
    }

    private static (int, int) Key(float x, float z) => ((int)MathF.Floor(x / Bucket), (int)MathF.Floor(z / Bucket));
}
