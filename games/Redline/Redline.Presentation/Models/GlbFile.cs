using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Redline.Presentation.Models;

/// <summary>A glTF node: its name, local transform and whether it carries a mesh.</summary>
public sealed record GlbNode(string Name, Vector3 Translation, Quaternion Rotation, Vector3 Scale, int? Mesh, IReadOnlyList<string> Children)
{
    /// <summary>The node's local axis <paramref name="axis"/> (e.g. <see cref="Vector3.UnitZ"/>) rotated into its parent's frame.</summary>
    public Vector3 Axis(Vector3 axis) => Vector3.Transform(axis, Rotation);
}

/// <summary>
/// Reads the JSON chunk of a binary glTF (.glb) without any engine: node names and transforms, material names and base
/// colours (linear, as stored), and triangle counts. Enough to check an exported model against its naming contract and
/// budgets in a unit test, with no Godot or Blender involved.
/// </summary>
public sealed class GlbFile
{
    private const uint Magic = 0x46546C67; // "glTF"
    private const uint JsonChunk = 0x4E4F534A; // "JSON"

    private GlbFile(Dictionary<string, GlbNode> nodes, List<string> materials, HashSet<string> usedMaterials,
        Dictionary<string, Vector4> baseColours, int triangles, IReadOnlyList<string> extensionsRequired)
    {
        Nodes = nodes;
        Materials = materials;
        UsedMaterials = usedMaterials;
        BaseColours = baseColours;
        Triangles = triangles;
        ExtensionsRequired = extensionsRequired;
    }

    /// <summary>Nodes by name (glTF requires no unique names; a later duplicate overwrites an earlier one).</summary>
    public IReadOnlyDictionary<string, GlbNode> Nodes { get; }

    /// <summary>Every material's name, in file order.</summary>
    public IReadOnlyList<string> Materials { get; }

    /// <summary>Names of the materials some mesh primitive actually uses.</summary>
    public IReadOnlySet<string> UsedMaterials { get; }

    /// <summary>Each material's <c>baseColorFactor</c> (linear RGBA, as glTF stores it).</summary>
    public IReadOnlyDictionary<string, Vector4> BaseColours { get; }

    /// <summary>Triangles drawn: every mesh counted once per node that instances it.</summary>
    public int Triangles { get; }

    public IReadOnlyList<string> ExtensionsRequired { get; }

    public static GlbFile Load(string path) => Parse(File.ReadAllBytes(path));

    /// <exception cref="FormatException">Not a glTF 2.0 binary, or no JSON chunk.</exception>
    public static GlbFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new FormatException("Not a binary glTF file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 2)
            throw new FormatException("Only glTF 2.0 is supported.");
        var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[16..]) != JsonChunk || 20 + chunkLength > data.Length)
            throw new FormatException("The first chunk is not JSON.");
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data.Slice(20, chunkLength)));
        return FromJson(doc.RootElement);
    }

    private static GlbFile FromJson(JsonElement root)
    {
        var accessors = Items(root, "accessors");
        var meshes = Items(root, "meshes");
        var materialsJson = Items(root, "materials");
        var nodesJson = Items(root, "nodes");

        var materials = materialsJson.Select((m, i) => Str(m, "name") ?? $"material{i}").ToList();
        var baseColours = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        for (var i = 0; i < materialsJson.Count; i++)
        {
            var c = new Vector4(1);
            if (materialsJson[i].TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.TryGetProperty("baseColorFactor", out var f))
                c = new Vector4(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle(), f[3].GetSingle());
            baseColours[materials[i]] = c;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var meshTriangles = new int[meshes.Count];
        for (var m = 0; m < meshes.Count; m++)
        {
            if (!meshes[m].TryGetProperty("primitives", out var prims))
                continue;
            foreach (var p in prims.EnumerateArray())
            {
                if (p.TryGetProperty("material", out var mat))
                    used.Add(materials[mat.GetInt32()]);
                if (p.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                    continue;
                var acc = p.TryGetProperty("indices", out var idx) ? idx.GetInt32() : p.GetProperty("attributes").GetProperty("POSITION").GetInt32();
                meshTriangles[m] += accessors[acc].GetProperty("count").GetInt32() / 3;
            }
        }

        var names = nodesJson.Select((n, i) => Str(n, "name") ?? $"node{i}").ToList();
        var nodes = new Dictionary<string, GlbNode>(StringComparer.Ordinal);
        var triangles = 0;
        for (var i = 0; i < nodesJson.Count; i++)
        {
            var n = nodesJson[i];
            int? mesh = n.TryGetProperty("mesh", out var me) ? me.GetInt32() : null;
            if (mesh is { } mi)
                triangles += meshTriangles[mi];
            var children = n.TryGetProperty("children", out var ch) ? ch.EnumerateArray().Select(c => names[c.GetInt32()]).ToList() : [];
            nodes[names[i]] = new GlbNode(names[i], Vec3(n, "translation", Vector3.Zero), Quat(n), Vec3(n, "scale", Vector3.One), mesh, children);
        }

        var required = root.TryGetProperty("extensionsRequired", out var ext) ? ext.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [];
        return new GlbFile(nodes, materials, used, baseColours, triangles, required);
    }

    private static List<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var a) ? [.. a.EnumerateArray()] : [];

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static Vector3 Vec3(JsonElement e, string name, Vector3 fallback) =>
        e.TryGetProperty(name, out var v) ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : fallback;

    private static Quaternion Quat(JsonElement e) =>
        e.TryGetProperty("rotation", out var r) ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
}
