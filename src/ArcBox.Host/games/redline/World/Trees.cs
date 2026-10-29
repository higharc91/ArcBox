using Godot;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Live oaks placed by <see cref="TreeScatter"/>, one <see cref="MultiMeshInstance3D"/> per variant under a "Trees" node:
/// the three Blender oaks (<see cref="TreeModel"/>, tree i drawn as variant i mod 3) or, when that model can't be used,
/// the procedural one (a short trunk under three wide, lumpy, squashed crowns). Unit crown radius, scaled per instance;
/// per-instance colour varies the green. All <see cref="UltraCount"/> trees are placed; a preset draws a prefix of each
/// variant (<see cref="SetFraction"/>), which together is about what <see cref="TreeScatter"/> would place for the
/// smaller count, so F2 can change it at runtime.
/// </summary>
public static class Trees
{
    public const int UltraCount = 1500;

    private static ArrayMesh? _mesh;
    private static ArrayMesh[]? _variants;
    private static bool _variantsTried;

    public static Node3D Build(CircuitAssets a, IReadOnlyList<Landmarks.Footprint> keepClear)
    {
        const int count = UltraCount;
        var trees = TreeScatter.Place(a.Samples, a.Ground, count, seed: 99, minTrackDistance: 46, blocked: p =>
            keepClear.Any(f => (p.X - f.X) * (p.X - f.X) + (p.Y - f.Z) * (p.Y - f.Z) < f.Radius * f.Radius));

        var meshes = Variants() ?? [Mesh()];
        var root = new Node3D { Name = "Trees" };
        for (var v = 0; v < meshes.Length; v++)
        {
            var mine = Enumerable.Range(0, trees.Count).Where(i => meshes.Length == 1 || TreeModel.VariantOf(i) == v).ToList();
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = meshes[v],
                InstanceCount = mine.Count,
            };
            for (var j = 0; j < mine.Count; j++)
            {
                var t = trees[mine[j]];
                var basis = new Basis(Vector3.Up, t.Yaw).Scaled(new Vector3(t.Scale, t.Scale * (0.85f + 0.2f * t.Shade), t.Scale));
                // Sink a little so trunks never float on slopes.
                mm.SetInstanceTransform(j, new Transform3D(basis, MeshConvert.V(t.Position) - new Vector3(0, 0.3f, 0)));
                var green = new Color("4a5a2a").Lerp(new Color("66702f"), t.Shade);
                mm.SetInstanceColor(j, green.Lightened((t.Shade - 0.5f) * 0.1f));
            }
            root.AddChild(new MultiMeshInstance3D { Multimesh = mm, Name = meshes.Length == 1 ? "Oaks" : TreeModel.Variants[v] });
        }
        return root;
    }

    /// <summary>Draws the first <paramref name="fraction"/> of each variant's trees.</summary>
    public static void SetFraction(Node3D trees, float fraction)
    {
        foreach (var mmi in trees.GetChildren().OfType<MultiMeshInstance3D>())
        {
            var mm = mmi.Multimesh;
            mm.VisibleInstanceCount = fraction >= 1 ? -1 : (int)(mm.InstanceCount * fraction);
        }
    }

    /// <summary>The Blender oaks' meshes with their bark and leaves materials, or null when the model can't be used.</summary>
    private static ArrayMesh[]? Variants()
    {
        if (_variantsTried)
        {
            return _variants;
        }
        _variantsTried = true;
        var root = SetPieces.Instantiate(LandmarkModels.Trees);
        if (root is null)
        {
            return null;
        }
        var meshes = TreeModel.Variants.Select(v => SetPieces.Mesh(root, v)?.Mesh as ArrayMesh).ToArray();
        root.Free();
        if (meshes.Any(m => m is null))
        {
            return null;
        }
        foreach (var m in meshes)
        {
            // A MultiMesh ignores per-instance overrides: the slots' materials go on the mesh itself.
            for (var i = 0; i < m!.GetSurfaceCount(); i++)
            {
                if (SetMaterials.For(ModelLibrary.SurfaceSlot(m, i)) is { } mat)
                {
                    m.SurfaceSetMaterial(i, mat);
                }
            }
        }
        _variants = [.. meshes.Select(m => m!)];
        return _variants;
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
