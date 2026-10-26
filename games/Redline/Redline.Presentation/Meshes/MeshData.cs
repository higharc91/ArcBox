using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// An indexed triangle mesh as plain <see cref="System.Numerics"/> data (right-handed, Y up, metres).
/// Every vertex has a position, normal, UV and colour; <see cref="Indices"/> holds three entries per triangle.
/// </summary>
public sealed class MeshData
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector2> Uvs { get; } = new();
    public List<Vector4> Colors { get; } = new();
    public List<int> Indices { get; } = new();
}
