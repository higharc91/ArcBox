using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// A piece of geometry in its own local frame. Triangles may be listed in either order: <see cref="CarMeshOps.Append"/>
/// orients each one from its vertex normals, so a part only has to get its normals pointing outward.
/// </summary>
internal sealed class Part
{
    public readonly List<Vector3> Positions = [];
    public readonly List<Vector3> Normals = [];
    public readonly List<Vector2> Uvs = [];
    public readonly List<Vector4> Colors = [];
    public readonly List<int> Indices = [];

    public int Vertex(Vector3 position, Vector3 normal, Vector2 uv, Vector4 colour)
    {
        Positions.Add(position);
        Normals.Add(CarMeshOps.SafeNormalize(normal));
        Uvs.Add(uv);
        Colors.Add(colour);
        return Positions.Count - 1;
    }

    public void Triangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    public void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    /// <summary>Overrides every vertex colour.</summary>
    public Part Paint(Vector4 colour)
    {
        for (var i = 0; i < Colors.Count; i++)
            Colors[i] = colour;
        return this;
    }
}

internal static class CarMeshOps
{
    private const float DegenerateArea2 = 1e-16f;

    public static Vector3 SafeNormalize(Vector3 v)
    {
        var len = v.Length();
        return len > 1e-12f && float.IsFinite(len) ? v / len : Vector3.UnitY;
    }

    public static Matrix4x4 At(Vector3 position) => Matrix4x4.CreateTranslation(position);

    public static Matrix4x4 At(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);

    /// <summary>
    /// Transforms <paramref name="part"/> by <paramref name="transform"/> and appends it to <paramref name="target"/>.
    /// Each output triangle is wound <b>clockwise as seen from the side its vertex normals point to</b> (Godot 4's front
    /// face): its right-handed cross product (b - a) x (c - a) points against the normals. Zero-area triangles are dropped.
    /// </summary>
    public static void Append(MeshData target, Part part, Matrix4x4 transform)
    {
        if (!Matrix4x4.Invert(transform, out var inverse))
            throw new ArgumentException("Transform is singular.", nameof(transform));
        var normalMatrix = Matrix4x4.Transpose(inverse);
        var start = target.Positions.Count;
        for (var i = 0; i < part.Positions.Count; i++)
        {
            target.Positions.Add(Vector3.Transform(part.Positions[i], transform));
            target.Normals.Add(SafeNormalize(Vector3.TransformNormal(part.Normals[i], normalMatrix)));
            target.Uvs.Add(part.Uvs[i]);
            target.Colors.Add(part.Colors[i]);
        }
        for (var t = 0; t < part.Indices.Count; t += 3)
        {
            int a = start + part.Indices[t], b = start + part.Indices[t + 1], c = start + part.Indices[t + 2];
            var pa = target.Positions[a];
            var cross = Vector3.Cross(target.Positions[b] - pa, target.Positions[c] - pa);
            if (cross.LengthSquared() < DegenerateArea2)
                continue;
            var n = target.Normals[a] + target.Normals[b] + target.Normals[c];
            target.Indices.Add(a);
            if (Vector3.Dot(cross, n) > 0)
            {
                target.Indices.Add(c);
                target.Indices.Add(b);
            }
            else
            {
                target.Indices.Add(b);
                target.Indices.Add(c);
            }
        }
    }

    public static void Append(MeshData target, Part part) => Append(target, part, Matrix4x4.Identity);
}
