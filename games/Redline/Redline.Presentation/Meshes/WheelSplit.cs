using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// Splits a car mesh that holds all four wheels (tyres or rims) into one mesh per wheel, each relative to its wheel centre
/// so the host can spin it about the axle (+X), plus (<c>Other</c>) whatever is not part of a wheel (mirror glass, exhaust).
/// </summary>
public static class WheelSplit
{
    /// <param name="mesh">Car-frame mesh.</param>
    /// <param name="centres"><see cref="CarMeshes.WheelCentres"/>.</param>
    /// <param name="radius">A triangle whose centroid is within this distance of a centre belongs to that wheel.</param>
    public static (IReadOnlyList<MeshData> Wheels, MeshData Other) Split(MeshData mesh, IReadOnlyList<Vector3> centres, float radius = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(centres);

        var wheels = centres.Select(_ => new MeshData()).ToArray();
        var rest = new MeshData();
        var maps = centres.Select(_ => new Dictionary<int, int>()).ToArray();
        var restMap = new Dictionary<int, int>();

        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var centroid = (mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c]) / 3;
            var best = -1;
            var bestDistance = radius;
            for (var w = 0; w < centres.Count; w++)
            {
                var d = Vector3.Distance(centroid, centres[w]);
                if (d <= bestDistance)
                    (best, bestDistance) = (w, d);
            }

            var (target, map, origin) = best >= 0 ? (wheels[best], maps[best], centres[best]) : (rest, restMap, Vector3.Zero);
            target.Indices.Add(Copy(a));
            target.Indices.Add(Copy(b));
            target.Indices.Add(Copy(c));

            int Copy(int i)
            {
                if (map.TryGetValue(i, out var j))
                    return j;
                j = target.Positions.Count;
                target.Positions.Add(mesh.Positions[i] - origin);
                target.Normals.Add(mesh.Normals[i]);
                target.Uvs.Add(mesh.Uvs[i]);
                target.Colors.Add(mesh.Colors[i]);
                map[i] = j;
                return j;
            }
        }
        return (wheels, rest);
    }
}
