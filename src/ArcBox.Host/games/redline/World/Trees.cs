using Godot;
using Redline.Presentation.Meshes;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Live oaks as one <see cref="MultiMeshInstance3D"/>: a short trunk under three wide, lumpy, squashed crowns (unit crown
/// radius, scaled per instance), placed by <see cref="TreeScatter"/>. Per-instance colour varies the green.
/// </summary>
public static class Trees
{
    public const int UltraCount = 1500;

    private static ArrayMesh? _mesh;

    public static MultiMeshInstance3D Build(CircuitAssets a, IReadOnlyList<Landmarks.Footprint> keepClear, float fraction)
    {
        var count = (int)(UltraCount * fraction);
        var trees = TreeScatter.Place(a.Samples, a.Ground, count, seed: 99, minTrackDistance: 46, blocked: p =>
            keepClear.Any(f => (p.X - f.X) * (p.X - f.X) + (p.Y - f.Z) * (p.Y - f.Z) < f.Radius * f.Radius));

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = Mesh(),
            InstanceCount = trees.Count,
        };
        for (var i = 0; i < trees.Count; i++)
        {
            var t = trees[i];
            var basis = new Basis(Vector3.Up, t.Yaw).Scaled(new Vector3(t.Scale, t.Scale * (0.85f + 0.2f * t.Shade), t.Scale));
            // Sink a little so trunks never float on slopes.
            mm.SetInstanceTransform(i, new Transform3D(basis, MeshConvert.V(t.Position) - new Vector3(0, 0.3f, 0)));
            var green = new Color("4a5a2a").Lerp(new Color("66702f"), t.Shade);
            mm.SetInstanceColor(i, green.Lightened((t.Shade - 0.5f) * 0.1f));
        }
        return new MultiMeshInstance3D { Multimesh = mm, Name = "Trees" };
    }

    private static ArrayMesh Mesh()
    {
        if (_mesh is not null)
        {
            return _mesh;
        }
        var trunk = new Prims()
            .Cylinder(new Vector3(0, -0.1f, 0), new Vector3(0, 0.62f, 0), 0.085f, 0.06f, 8, new Color("4a3b2c"))
            .Cylinder(new Vector3(0, 0.4f, 0), new Vector3(0.38f, 0.75f, 0.1f), 0.045f, 0.03f, 6, new Color("4a3b2c"))
            .Cylinder(new Vector3(0, 0.45f, 0), new Vector3(-0.32f, 0.8f, -0.18f), 0.045f, 0.03f, 6, new Color("4a3b2c"));
        var mesh = trunk.Commit();
        mesh.SurfaceSetMaterial(0, Materials.Plain("4a3b2c", 0.95f));

        var lumps = new FastNoiseLite { Seed = 4, Frequency = 1.4f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
        var crowns = new Prims()
            .Ellipsoid(new Vector3(0, 0.95f, 0), new Vector3(1, 0.55f, 0.95f), 18, 10, Colors.White, lumps, 0.22f)
            .Ellipsoid(new Vector3(0.55f, 0.82f, 0.25f), new Vector3(0.66f, 0.42f, 0.62f), 14, 8, new Color(0.92f, 0.95f, 0.9f), lumps, 0.25f)
            .Ellipsoid(new Vector3(-0.52f, 0.86f, -0.3f), new Vector3(0.7f, 0.46f, 0.66f), 14, 8, new Color(0.88f, 0.9f, 0.86f), lumps, 0.25f);
        crowns.Commit(mesh);
        mesh.SurfaceSetMaterial(1, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.92f,
            MetallicSpecular = 0.2f,
            BacklightEnabled = true,
            Backlight = new Color(0.18f, 0.22f, 0.08f),
        });
        _mesh = mesh;
        return mesh;
    }
}
