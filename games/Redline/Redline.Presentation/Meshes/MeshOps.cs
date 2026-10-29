using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>Small helpers for building <see cref="MeshData"/> under its clockwise-front winding convention.</summary>
internal static class MeshOps
{
    /// <summary>Appends a vertex (normal left zero for <see cref="ComputeNormals"/>) and returns its index.</summary>
    public static int AddVertex(this MeshData m, Vector3 position, Vector2 uv, Vector4 color)
    {
        m.Positions.Add(position);
        m.Normals.Add(Vector3.Zero);
        m.Uvs.Add(uv);
        m.Colors.Add(color);
        return m.Positions.Count - 1;
    }

    /// <summary>Adds triangle (a, b, c), which must be clockwise seen from its front.</summary>
    public static void AddTriangle(this MeshData m, int a, int b, int c)
    {
        m.Indices.Add(a);
        m.Indices.Add(b);
        m.Indices.Add(c);
    }

    /// <summary>Adds the quad v0 → v1 → v2 → v3, given clockwise seen from its front, as two triangles.</summary>
    public static void AddQuad(this MeshData m, int v0, int v1, int v2, int v3)
    {
        m.AddTriangle(v0, v1, v3);
        m.AddTriangle(v1, v2, v3);
    }

    /// <summary>Area-weighted smooth normals from the winding: each face contributes Cross(c - a, b - a).</summary>
    public static void ComputeNormals(this MeshData m)
    {
        var acc = new Vector3[m.Positions.Count];
        for (var i = 0; i < m.Indices.Count; i += 3)
        {
            int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
            var face = Vector3.Cross(m.Positions[c] - m.Positions[a], m.Positions[b] - m.Positions[a]);
            acc[a] += face;
            acc[b] += face;
            acc[c] += face;
        }
        for (var i = 0; i < acc.Length; i++)
        {
            var len = acc[i].Length();
            m.Normals[i] = len > 1e-12f ? acc[i] / len : Vector3.UnitY;
        }
    }

    public static Vector4 Hex(string rgb) => new(
        Convert.ToInt32(rgb.Substring(1, 2), 16) / 255f,
        Convert.ToInt32(rgb.Substring(3, 2), 16) / 255f,
        Convert.ToInt32(rgb.Substring(5, 2), 16) / 255f,
        1f);
}
