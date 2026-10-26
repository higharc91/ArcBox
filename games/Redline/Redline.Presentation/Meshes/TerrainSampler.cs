using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// Height of a <see cref="TerrainBuilder"/> grid at any (x, z), interpolated on the same two triangles per cell that the mesh
/// draws (diagonal top-right to bottom-left), so objects placed with it sit exactly on the rendered ground.
/// </summary>
public sealed class TerrainSampler
{
    private readonly float[] _h;
    private readonly int _cols;
    private readonly int _rows;

    private TerrainSampler(float[] heights, int cols, int rows, float x0, float z0, float cell)
    {
        _h = heights;
        _cols = cols;
        _rows = rows;
        X0 = x0;
        Z0 = z0;
        Cell = cell;
    }

    public float X0 { get; }
    public float Z0 { get; }
    public float Cell { get; }
    public float X1 => X0 + (_cols - 1) * Cell;
    public float Z1 => Z0 + (_rows - 1) * Cell;

    /// <summary>Reads the row-major grid layout <see cref="TerrainBuilder.Build"/> produces.</summary>
    public static TerrainSampler FromGrid(MeshData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var p = terrain.Positions;
        if (p.Count < 4)
            throw new ArgumentException("Terrain grid needs at least 2 × 2 vertices.", nameof(terrain));
        var cols = 1;
        while (cols < p.Count && p[cols].Z == p[0].Z)
            cols++;
        if (cols < 2 || p.Count % cols != 0)
            throw new ArgumentException("Terrain is not a row-major grid.", nameof(terrain));
        var heights = new float[p.Count];
        for (var i = 0; i < p.Count; i++)
            heights[i] = p[i].Y;
        return new TerrainSampler(heights, cols, p.Count / cols, p[0].X, p[0].Z, p[1].X - p[0].X);
    }

    /// <summary>Ground height at (x, z); outside the grid the edge height continues.</summary>
    public float Height(float x, float z)
    {
        var fx = Math.Clamp((x - X0) / Cell, 0, _cols - 1.0001f);
        var fz = Math.Clamp((z - Z0) / Cell, 0, _rows - 1.0001f);
        int ix = (int)fx, iz = (int)fz;
        float u = fx - ix, v = fz - iz;
        var tl = _h[iz * _cols + ix];
        var tr = _h[iz * _cols + ix + 1];
        var bl = _h[(iz + 1) * _cols + ix];
        var br = _h[(iz + 1) * _cols + ix + 1];
        return u + v <= 1
            ? tl + (tr - tl) * u + (bl - tl) * v
            : br + (bl - br) * (1 - u) + (tr - br) * (1 - v);
    }

    public bool Contains(Vector2 p) => p.X >= X0 && p.X <= X1 && p.Y >= Z0 && p.Y <= Z1;
}
