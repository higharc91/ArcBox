using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// An indexed triangle mesh in engine-neutral form, ready to copy one-to-one into a Godot <c>ArrayMesh</c> surface.
/// <list type="bullet">
/// <item>Right-handed, Y up, metres. Circuit <c>(x, y)</c> maps to <c>(x, height, y)</c>: +X east, +Z south, so -Z is north.</item>
/// <item><b>Winding:</b> every triangle in <see cref="Indices"/> is <b>clockwise when seen from its front (visible) side</b>,
/// Godot 4's ArrayMesh convention, so the default back-face culling keeps the front. Equivalently, for a triangle
/// <c>(a, b, c)</c>, <c>Vector3.Cross(b - a, c - a)</c> points <b>away</b> from the front side: on the asphalt it points
/// down (-Y) and on a wall it points away from the track.</item>
/// <item><see cref="Normals"/> are unit length and point out of the front side (opposite that cross product).</item>
/// <item><see cref="Colors"/> are sRGB RGBA in 0..1 (the material sets <c>vertex_color_is_srgb</c>).</item>
/// <item><see cref="Uvs"/> are in metres unless the builder says otherwise; materials set their own tiling.</item>
/// </list>
/// <see cref="Positions"/>, <see cref="Normals"/>, <see cref="Uvs"/> and <see cref="Colors"/> have one entry per vertex;
/// <see cref="Custom"/> is either empty or has one entry per vertex too (per-vertex shader data, Godot's <c>CUSTOM0</c>,
/// whose meaning the builder documents). <see cref="Indices"/> holds three entries per triangle.
/// </summary>
public sealed class MeshData
{
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector2> Uvs { get; } = new();
    public List<Vector4> Colors { get; } = new();
    public List<Vector4> Custom { get; } = new();
    public List<int> Indices { get; } = new();
}
