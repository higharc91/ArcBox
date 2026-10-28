using Godot;
using Redline.Presentation.Meshes;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Copies engine-neutral <see cref="MeshData"/> into Godot meshes one-to-one. MeshData already uses Godot's conventions
/// (Y up, metres, clockwise front faces), so nothing is flipped; vertex colours stay sRGB and the materials say so.
/// </summary>
public static class MeshConvert
{
    /// <summary>One triangle surface with positions, normals, UVs, vertex colours and, if present, CUSTOM0 data.</summary>
    public static ArrayMesh ToArrayMesh(MeshData data, ArrayMesh? addTo = null)
    {
        var mesh = addTo ?? new ArrayMesh();
        if (data.Indices.Count == 0)
        {
            return mesh;
        }
        // MeshData.Custom, when present, goes to CUSTOM0 as four floats per vertex.
        var flags = data.Custom.Count == data.Positions.Count
            ? (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            : 0;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Arrays(data), null, null, flags);
        return mesh;
    }

    public static Godot.Collections.Array Arrays(MeshData data)
    {
        var n = data.Positions.Count;
        var positions = new Vector3[n];
        var normals = new Vector3[n];
        var uvs = new Vector2[n];
        var colours = new Color[n];
        for (var i = 0; i < n; i++)
        {
            positions[i] = V(data.Positions[i]);
            normals[i] = V(data.Normals[i]);
            uvs[i] = new Vector2(data.Uvs[i].X, data.Uvs[i].Y);
            var c = data.Colors[i];
            colours[i] = new Color(c.X, c.Y, c.Z, c.W);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices.ToArray();
        if (data.Custom.Count == n)
        {
            var custom = new float[n * 4];
            for (var i = 0; i < n; i++)
            {
                var c = data.Custom[i];
                (custom[4 * i], custom[4 * i + 1], custom[4 * i + 2], custom[4 * i + 3]) = (c.X, c.Y, c.Z, c.W);
            }
            arrays[(int)Mesh.ArrayType.Custom0] = custom;
        }
        return arrays;
    }

    public static Vector3 V(NVector3 v) => new(v.X, v.Y, v.Z);

    public static NVector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector2 V(NVector2 v) => new(v.X, v.Y);
}
