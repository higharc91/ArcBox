using System.Diagnostics;
using Godot;
using Redline.Presentation.Course;
using Redline.Presentation.Models;
using NVector2 = System.Numerics.Vector2;
using Plan = Redline.Presentation.Course.Surroundings;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// COTA's surroundings (spec §2.4) from <see cref="Plan"/>: the terrain's dry-grass material, the far land, the service
/// roads, car parks with parked cars, hospitality compounds with tents, the Grand Plaza and amphitheatre beside the
/// Observation Tower, scattered shrubs and the hazy Austin skyline. Draped surfaces are meshes from
/// <see cref="SurroundMeshes"/>; props are the Blender models (<see cref="SurroundingsModels"/>) as one MultiMesh per
/// variant, or simple procedural stand-ins when a model can't be used. The plan and meshes are built once per circuit.
/// The Low preset draws half the parked cars, shrubs and tree clumps (<see cref="SetDensity"/>).
/// <c>REDLINE_NO_SURROUNDINGS=1</c> leaves all of it out (comparison shots).
/// </summary>
public static class Surroundings
{
    public const string NodeName = "Surroundings";

    private sealed record Built(SurroundingsPlan Plan, ArrayMesh Roads, ArrayMesh Lots, ArrayMesh Plaza, ArrayMesh Terrain, ArrayMesh FarLand);

    private static readonly Dictionary<string, Built> Cache = new(StringComparer.Ordinal);
    private static readonly bool Disabled = System.Environment.GetEnvironmentVariable("REDLINE_NO_SURROUNDINGS") == "1";

    /// <summary>Starts loading the surroundings' models in the background (safe to call repeatedly).</summary>
    public static void Prewarm() => ModelLibrary.Prewarm([.. SurroundingsModels.All.Select(c => c.File)]);

    /// <summary>
    /// Builds the surroundings under one node and puts the terrain material on <paramref name="terrain"/>. The plaza
    /// complex clears its own ground of trees (<see cref="SurroundingsPlan.Clearings"/>): those instances are taken
    /// out of <paramref name="trees"/>' MultiMeshes; every other tree stays exactly where it was.
    /// </summary>
    public static Node3D Build(CircuitAssets a, IReadOnlyList<Landmarks.Footprint> footprints, Node3D trees, MeshInstance3D terrain)
    {
        var root = new Node3D { Name = NodeName };
        if (Disabled)
        {
            return root;
        }
        Prewarm();
        var b = Get(a, footprints, trees);
        ClearTrees(trees, b.Plan.Clearings);

        terrain.Mesh = b.Terrain;
        terrain.MaterialOverride = TerrainMaterials.Ground;
        root.AddChild(Surface("FarLand", b.FarLand, TerrainMaterials.Ground));
        root.AddChild(Surface("Roads", b.Roads, TerrainMaterials.Roads));
        root.AddChild(Surface("CarParks", b.Lots, TerrainMaterials.Lots));
        root.AddChild(Surface("PlazaPaving", b.Plaza, TerrainMaterials.Plaza));

        var plan = b.Plan;
        root.AddChild(Amphitheatre(plan.Plaza));
        root.AddChild(Instances("PlazaProps", SurroundingsModels.Plaza, plan.PlazaProps, _ => Colors.White, shadows: true));
        root.AddChild(Instances("Tents", SurroundingsModels.Tent, plan.Tents, _ => Colors.White, shadows: true));
        root.AddChild(Instances("ParkedCars", SurroundingsModels.ParkedCars, plan.ParkedCars, p => CarColour(p.Shade), shadows: true, density: true));
        root.AddChild(Instances("Shrubs", SurroundingsModels.Shrub, plan.Shrubs, p => ShrubColour(p.Shade), shadows: true, density: true));
        root.AddChild(Instances("Groves", SurroundingsModels.Grove, plan.Groves, p => GroveColour(p.Shade), shadows: true, density: true));
        root.AddChild(Instances("FarClumps", SurroundingsModels.FarClump, plan.FarClumps, p => GroveColour(p.Shade).Darkened(0.1f), shadows: false, density: true));
        root.AddChild(Skyline(plan.Skyline));
        return root;
    }

    /// <summary>Draws all (1) or half (Low) of the parked cars, shrubs, groves and far clumps; their shadows go on Low too.</summary>
    public static void SetDensity(Node world, Preset preset)
    {
        if (world.FindChild(NodeName, recursive: true, owned: false) is not Node3D root)
        {
            return;
        }
        var fraction = RenderPreset.TreeFraction(preset);
        foreach (var group in root.GetChildren().OfType<Node3D>().Where(n => n.HasMeta("density")))
        {
            var shadows = group.GetMeta("density").AsBool() && preset != Preset.Low;
            foreach (var mmi in group.GetChildren().OfType<MultiMeshInstance3D>())
            {
                mmi.Multimesh.VisibleInstanceCount = fraction >= 1 ? -1 : (int)(mmi.Multimesh.InstanceCount * fraction);
                mmi.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }
    }

    private static Built Get(CircuitAssets a, IReadOnlyList<Landmarks.Footprint> footprints, Node3D trees)
    {
        var key = a.Circuit.Name;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var sw = Stopwatch.StartNew();
        var pts = a.Circuit.Points.Skip(128).Take(24).ToList();
        // The tower's site (Landmarks.TowerSite): the footprint nearest the middle of those points.
        var guess = new NVector2(pts.Average(p => p.X), pts.Average(p => p.Y));
        var tower = footprints.Select(f => new NVector2(f.X, f.Z)).MinBy(f => NVector2.Distance(f, guess));
        var site = new SurroundingsSite(a.Samples, a.Ground, a.Circuit.LapLength, tower,
            [.. footprints.Select(f => new KeepOut(new NVector2(f.X, f.Z), f.Radius))], TreeKeepOuts(trees));
        var plan = Plan.Plan(site);
        var planMs = sw.ElapsedMilliseconds;
        var surfaces = SurroundMeshes.Build(plan, a.Ground);
        var terrain = TerrainMaterials.Terrain(a.Terrain, SurroundMeshes.TerrainAttributes(a.Terrain, a.Samples, plan));
        var far = new SurroundHorizon(a.Ground, a.Samples).Build();
        var farMesh = TerrainMaterials.Terrain(far, [.. Enumerable.Repeat(new TerrainAttribute(SurroundMeshes.DistanceCap, 0, false, 0), far.Positions.Count)]);
        GD.Print($"REDLINE surroundings planned in {planMs} ms, built in {sw.ElapsedMilliseconds} ms: {plan.CarParks.Count} car parks, "
            + $"{plan.ParkedCars.Count} cars, {plan.Compounds.Count} compounds, {plan.Tents.Count} tents, {plan.Roads.Count} roads, "
            + $"{plan.Shrubs.Count} shrubs, {plan.Groves.Count} groves, {plan.FarClumps.Count} far clumps, {plan.PlazaProps.Count} plaza props, "
            + $"{site.Trees.Count - plan.Trees.Count} trees cleared");
        var built = new Built(plan, MeshConvert.ToArrayMesh(surfaces.Roads), MeshConvert.ToArrayMesh(surfaces.Lots),
            MeshConvert.ToArrayMesh(surfaces.Plaza), terrain, farMesh);
        Cache[key] = built;
        return built;
    }

    /// <summary>Drops the tree instances whose trunk stands in a clearing (order kept, so a preset's prefix still mixes).</summary>
    private static void ClearTrees(Node3D trees, IReadOnlyList<KeepOut> clearings)
    {
        if (clearings.Count == 0)
        {
            return;
        }
        foreach (var mmi in trees.GetChildren().OfType<MultiMeshInstance3D>())
        {
            var mm = mmi.Multimesh;
            var keep = Enumerable.Range(0, mm.InstanceCount).Where(i =>
            {
                var o = mm.GetInstanceTransform(i).Origin;
                return !clearings.Any(c => NVector2.Distance(c.Centre, new NVector2(o.X, o.Z)) < c.Radius);
            }).Select(i => (mm.GetInstanceTransform(i), mm.GetInstanceColor(i))).ToList();
            if (keep.Count == mm.InstanceCount)
            {
                continue;
            }
            var visible = mm.VisibleInstanceCount;
            var next = new MultiMesh { TransformFormat = mm.TransformFormat, UseColors = mm.UseColors, Mesh = mm.Mesh, InstanceCount = keep.Count };
            for (var i = 0; i < keep.Count; i++)
            {
                next.SetInstanceTransform(i, keep[i].Item1);
                next.SetInstanceColor(i, keep[i].Item2);
            }
            next.VisibleInstanceCount = visible < 0 ? -1 : (int)((long)visible * keep.Count / Math.Max(1, mm.InstanceCount));
            mmi.Multimesh = next;
        }
    }

    /// <summary>The trees' trunks and crown radii, read back from the tree MultiMeshes.</summary>
    private static List<KeepOut> TreeKeepOuts(Node3D trees)
    {
        var list = new List<KeepOut>();
        foreach (var mmi in trees.GetChildren().OfType<MultiMeshInstance3D>())
        {
            var mm = mmi.Multimesh;
            for (var i = 0; i < mm.InstanceCount; i++)
            {
                var xf = mm.GetInstanceTransform(i);
                list.Add(new KeepOut(new NVector2(xf.Origin.X, xf.Origin.Z), xf.Basis.Column0.Length()));
            }
        }
        return list;
    }

    private static MeshInstance3D Surface(string name, ArrayMesh mesh, Material material) => new()
    {
        Name = name,
        Mesh = mesh,
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    // ---- props --------------------------------------------------------------------------------------------

    /// <summary>One MultiMesh per variant of <paramref name="contract"/>'s model (or of the procedural stand-ins).</summary>
    private static Node3D Instances(string name, ModelContract contract, IReadOnlyList<Prop> props, Func<Prop, Color> colour, bool shadows, bool density = false)
    {
        var node = new Node3D { Name = name };
        if (density)
        {
            node.SetMeta("density", shadows);
        }
        var meshes = Variants(contract) ?? Fallbacks(contract);
        for (var v = 0; v < meshes.Length; v++)
        {
            var mine = props.Where(p => p.Variant == v).ToList();
            if (mine.Count == 0)
            {
                continue;
            }
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = meshes[v],
                InstanceCount = mine.Count,
            };
            for (var i = 0; i < mine.Count; i++)
            {
                var p = mine[i];
                var basis = new Basis(Vector3.Up, p.Yaw).Scaled(new Vector3(p.Scale, p.Scale, p.Scale));
                mm.SetInstanceTransform(i, new Transform3D(basis, MeshConvert.V(p.Position) - new Vector3(0, 0.05f, 0)));
                mm.SetInstanceColor(i, colour(p));
            }
            node.AddChild(new MultiMeshInstance3D
            {
                Name = contract.Nodes[v],
                Multimesh = mm,
                CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        return node;
    }

    private static readonly Dictionary<string, ArrayMesh[]?> VariantCache = new(StringComparer.Ordinal);

    /// <summary>The model's variant meshes with their slots' materials on the meshes (a MultiMesh ignores overrides).</summary>
    private static ArrayMesh[]? Variants(ModelContract contract)
    {
        if (VariantCache.TryGetValue(contract.File, out var cached))
        {
            return cached;
        }
        ArrayMesh[]? result = null;
        if (SetPieces.Instantiate(contract) is { } root)
        {
            var meshes = contract.Nodes.Select(n => SetPieces.Mesh(root, n)?.Mesh as ArrayMesh).ToArray();
            root.Free();
            if (meshes.All(m => m is not null))
            {
                foreach (var m in meshes)
                {
                    for (var i = 0; i < m!.GetSurfaceCount(); i++)
                    {
                        if (Slot(ModelLibrary.SurfaceSlot(m, i)) is { } mat)
                        {
                            m.SurfaceSetMaterial(i, mat);
                        }
                    }
                }
                result = [.. meshes.Select(m => m!)];
            }
        }
        VariantCache[contract.File] = result;
        return result;
    }

    private static Node3D Amphitheatre(GrandPlaza p)
    {
        var axis = new Vector3(p.AmphitheatreAxis.X, 0, p.AmphitheatreAxis.Y);
        var basis = new Basis(new Vector3(axis.Z, 0, -axis.X), Vector3.Up, axis);
        var xf = new Transform3D(basis, MeshConvert.V(p.AmphitheatreOrigin));
        if (SetPieces.Instantiate(SurroundingsModels.Amphitheatre, special: Slot) is { } model)
        {
            model.Name = "Amphitheatre";
            model.Transform = xf;
            return model;
        }
        // Stand-in: a stage, a back wall and a stepped lawn fan.
        var prims = new Prims();
        prims.Box(new Vector3(0, -3.2f, -6.5f), new Vector3(26, 9.6f, 13), new Color("1c1d23"));
        prims.Box(new Vector3(0, 5.5f, -14), new Vector3(31, 11, 1.4f), new Color("16171c"));
        for (var k = 0; k < 11; k++)
        {
            var r = 10 + (3 * k);
            prims.Box(new Vector3(0, (0.5f * (k + 1)) - 4, r - 2.5f), new Vector3(2 * r * 0.75f, 8, 3), new Color("6f8a3c"));
        }
        return new MeshInstance3D { Name = "Amphitheatre", Mesh = prims.Commit(), Transform = xf, MaterialOverride = Materials.VertexColour(0.9f, 0, "amphi") };
    }

    private static Node3D Skyline(SkylineSite s)
    {
        var facing = new Vector3(s.Facing.X, 0, s.Facing.Y).Normalized();
        var basis = new Basis(new Vector3(facing.Z, 0, -facing.X), Vector3.Up, facing).Scaled(new Vector3(s.Scale, s.Scale, s.Scale));
        var xf = new Transform3D(basis, MeshConvert.V(s.Position));
        Node3D node;
        if (SetPieces.Instantiate(SurroundingsModels.Skyline, special: Slot) is { } model)
        {
            node = model;
        }
        else
        {
            var prims = new Prims();
            var rng = new Random(5);
            for (var i = 0; i < 40; i++)
            {
                var x = (float)(rng.NextDouble() - 0.5) * 1600;
                var h = 40 + (float)(rng.NextDouble() * 170 * Math.Exp(-x * x / 3e5));
                prims.Box(new Vector3(x, (h / 2) - 20, (float)(rng.NextDouble() - 0.5) * 400), new Vector3(40, h + 20, 40), new Color("8c96a3"));
            }
            node = new MeshInstance3D { Mesh = prims.Commit(), MaterialOverride = Materials.VertexColour(0.8f, 0, "skyline") };
        }
        node.Name = "Skyline";
        node.Transform = xf;
        foreach (var mi in node.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Append(node as MeshInstance3D))
        {
            if (mi is not null)
            {
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }
        return node;
    }

    /// <summary>Simple stand-ins per variant when a props model can't be used.</summary>
    private static ArrayMesh[] Fallbacks(ModelContract contract)
    {
        var white = new Color("eeeeea");
        var mat = Materials.VertexColour(0.7f, 0, "surround-fallback");
        ArrayMesh Make(Prims p)
        {
            var m = p.Commit();
            m.SurfaceSetMaterial(0, mat);
            return m;
        }
        return contract.Nodes.Select(n => Make(n switch
        {
            "light_pole" => new Prims().Cylinder(Vector3.Zero, new Vector3(0, 8, 0), 0.09f, 0.08f, 6, new Color("383b42")),
            "planter" => new Prims().Box(new Vector3(0, 0.3f, 0), new Vector3(2.4f, 0.7f, 2.4f), new Color("c4c1b9"))
                .Ellipsoid(new Vector3(0, 2.8f, 0), new Vector3(1.1f, 0.8f, 1.1f), 8, 5, new Color("5d6b33")),
            "kiosk" => new Prims().Box(new Vector3(0, 1.3f, 0), new Vector3(4.6f, 2.8f, 3.2f), white),
            "flag_pole" => new Prims().Cylinder(Vector3.Zero, new Vector3(0, 12, 0), 0.06f, 0.05f, 6, new Color("dcdfe3"))
                .Box(new Vector3(0.8f, 9.3f, 0), new Vector3(1.5f, 4.6f, 0.04f), new Color("d0101c")),
            "marquee" => new Prims().Box(new Vector3(0, 1.5f, 0), new Vector3(21, 3, 11), white)
                .Box(new Vector3(0, 3.9f, 0), new Vector3(21.4f, 1.8f, 7), white),
            "pagoda" => new Prims().Box(new Vector3(0, 1.25f, 0), new Vector3(6, 2.5f, 6), white),
            "grove_oak" or "grove_cedar" or "far_clump" => new Prims().Ellipsoid(new Vector3(0, 0.5f, 0), new Vector3(1, 0.55f, 1), 7, 4, Colors.White),
            "sedan" or "hatch" => new Prims().Box(new Vector3(0, 0.6f, 0), new Vector3(1.8f, 0.7f, 4.6f), Colors.White)
                .Box(new Vector3(0, 1.15f, -0.2f), new Vector3(1.5f, 0.5f, 2.2f), new Color("303540")),
            "suv" or "pickup" => new Prims().Box(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 0.9f, 5.2f), Colors.White)
                .Box(new Vector3(0, 1.45f, 0.2f), new Vector3(1.7f, 0.6f, 2.4f), new Color("303540")),
            _ => new Prims().Ellipsoid(new Vector3(0, 0.4f, 0), new Vector3(1, 0.7f, 1), 8, 5, Colors.White),
        })).ToArray();
    }

    // ---- materials ----------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Material> MatCache = new(StringComparer.Ordinal);

    /// <summary>The surroundings' own slots; everything else is <see cref="SetMaterials"/>' (concrete, steel, glass, LEDs …).</summary>
    private static Material? Slot(string slot) => slot switch
    {
        "lawn" => Cached(slot, () => new StandardMaterial3D
        {
            AlbedoColor = new Color("6f8a3c"),
            AlbedoTexture = Materials.Noise(256, 0.08f, 81, FastNoiseLite.NoiseTypeEnum.Value, 3),
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.3f, 0.3f, 0.3f),
            Roughness = 0.95f,
        }),
        "paving" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("cfc6b6"), Roughness = 0.8f }),
        "canopy" => Cached(slot, () => new StandardMaterial3D
        {
            AlbedoColor = new Color("f2f2ee"),
            Roughness = 0.55f,
            BacklightEnabled = true,
            Backlight = new Color(0.35f, 0.35f, 0.33f),
        }),
        "screen" => Materials.Emissive("2a4f8a", 1.6f),
        "canvas" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("f4f3ef"), Roughness = 0.65f, BacklightEnabled = true, Backlight = new Color(0.3f, 0.3f, 0.28f) }),
        "deck" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("8a6a48"), Roughness = 0.8f }),
        "kiosk" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("eeeeea"), Roughness = 0.45f }),
        "shrub" => Cached(slot, () => new StandardMaterial3D
        {
            // White under the per-instance colour (the shrub's shade).
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = Materials.Noise(256, 0.12f, 83, FastNoiseLite.NoiseTypeEnum.Cellular, 2),
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(3, 3, 3),
            Roughness = 0.92f,
            MetallicSpecular = 0.2f,
            BacklightEnabled = true,
            Backlight = new Color(0.15f, 0.18f, 0.06f),
        }),
        "grove" => Cached(slot, () => new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = Materials.Noise(256, 0.1f, 85, FastNoiseLite.NoiseTypeEnum.Cellular, 2),
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.6f, 0.6f, 0.6f),
            Roughness = 0.92f,
            MetallicSpecular = 0.2f,
            BacklightEnabled = true,
            Backlight = new Color(0.12f, 0.15f, 0.05f),
        }),
        "car_paint" => Cached(slot, () => new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Metallic = 0.35f,
            Roughness = 0.3f,
            ClearcoatEnabled = true,
            Clearcoat = 0.6f,
        }),
        "car_glass" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("141a22"), Metallic = 0.6f, Roughness = 0.08f }),
        "car_dark" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("1b1c1f"), Roughness = 0.8f }),
        "skyline" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("a3abb6"), Roughness = 0.7f }),
        "skyline_glass" => Cached(slot, () => new StandardMaterial3D { AlbedoColor = new Color("56657a"), Metallic = 0.5f, Roughness = 0.2f }),
        _ => SetMaterials.For(slot),
    };

    private static Material Cached(string key, Func<Material> make)
    {
        if (!MatCache.TryGetValue(key, out var m))
        {
            MatCache[key] = m = make();
        }
        return m;
    }

    /// <summary>Car colours as they are in a car park: mostly white, black, grey and silver, some blue and red.</summary>
    private static Color CarColour(float shade) => shade switch
    {
        < 0.26f => new Color("e9eaec"),
        < 0.42f => new Color("17181b"),
        < 0.56f => new Color("6c7077"),
        < 0.69f => new Color("b4b8bd"),
        < 0.78f => new Color("1f3a66"),
        < 0.86f => new Color("8e1a1a"),
        < 0.91f => new Color("8a7a62"),
        < 0.95f => new Color("2f4a3a"),
        _ => new Color("c4762a"),
    };

    /// <summary>Live oak and cedar greens, darker than the field.</summary>
    private static Color GroveColour(float shade) =>
        new Color("3a4a22").Lerp(new Color("57622c"), shade).Lightened((shade - 0.5f) * 0.06f);

    private static Color ShrubColour(float shade) =>
        new Color("4f5a2a").Lerp(new Color("75703a"), shade).Lightened((shade - 0.5f) * 0.08f);
}
