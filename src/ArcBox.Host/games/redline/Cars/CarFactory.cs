using ArcBox.Host.Games.Redline.World;
using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// Builds <see cref="CarNode"/>s. The Blender car (<c>models/car_2026.glb</c> + <c>car_2026_lod1.glb</c>, see
/// <see cref="CarModel"/>) is instanced from <see cref="ModelLibrary"/> with its named material slots bound to the team's
/// livery (<see cref="CarMaterials"/>), the number drawn at its number empties and the helmet at its helmet empty. When
/// the model is missing or broken, the car falls back to the cached procedural meshes (<see cref="RedlineAssets.Cars"/>),
/// converted once per driver. Livery colours, numbers, T-cams and the matte/gloss finish all come from grid2026.json
/// through <c>Grid2026</c>.
/// </summary>
public static class CarFactory
{
    private sealed record GodotCar(
        ArrayMesh Body, ArrayMesh Carbon, ArrayMesh Halo, ArrayMesh Lights, ArrayMesh Helmet, ArrayMesh RimsOther,
        ArrayMesh[] Tyres, ArrayMesh[] Rims);

    private static readonly Dictionary<string, GodotCar> Meshes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ArrayMesh> Helmets = new(StringComparer.Ordinal);
    private static GradientTexture2D? _contactShadow;

    /// <summary>Starts loading the Blender car in the background (safe to call repeatedly).</summary>
    public static void Prewarm() => ModelLibrary.Prewarm(CarModel.Lod0File, CarModel.Lod1File);

    /// <summary>True unless the Blender car is known to be unusable, i.e. whether the procedural meshes can be skipped.</summary>
    public static bool ModelLikelyAvailable => ModelLibrary.LikelyAvailable(CarModel.Lod0File);

    public static CarNode Create(string driverCode)
    {
        var seat = Grid2026.Load().ByCode(driverCode);
        return FromModel(seat) ?? Procedural(driverCode);
    }

    // ---- Blender car -------------------------------------------------------------------------------------

    private static CarNode? FromModel(Seat seat)
    {
        var lod0 = ModelLibrary.Instantiate(CarModel.Lod0File, n => Missing(n, lod1: false));
        if (lod0 is null)
        {
            return null;
        }
        var lod1 = ModelLibrary.Instantiate(CarModel.Lod1File, n => Missing(n, lod1: true));
        var code = seat.Driver.Code;
        var car = new CarNode { Name = $"Car_{code}", Code = code };
        foreach (var (root, name) in new[] { (lod0, "Lod0"), (lod1, "Lod1") })
        {
            if (root is null)
            {
                continue;
            }
            root.Name = name;
            car.AddChild(root);
            Bind(root, seat);
            foreach (var wheel in CarModel.Wheels)
            {
                var pivot = root.GetNode<Node3D>(wheel);
                car.RegisterWheel(pivot, pivot.Position.Y);
            }
        }
        car.SetLods(lod0, lod1);
        car.SetLowDetail(false);

        AddNumbers(car, seat, [.. CarModel.Numbers.Select(n => lod0.GetNode<Node3D>(n).Transform.Orthonormalized())], CarModel.NumberSizes);
        car.AddChild(new MeshInstance3D
        {
            Name = "Helmet",
            Mesh = Helmet(seat),
            MaterialOverride = Materials.Helmet,
            Layers = CarNode.CarLayer,
            Position = lod0.GetNode<Node3D>(CarModel.Helmet).Position,
        });
        car.AddChild(ContactShadow());
        car.SetSmoke(new SpinSmoke { Position = new Vector3(0, 0.25f, -1.75f) });
        return car;
    }

    private static IReadOnlyList<string> Missing(Node3D root, bool lod1)
    {
        var (nodes, mats) = ModelLibrary.Names(root);
        return CarModel.Missing(nodes, mats, lod1);
    }

    /// <summary>Car layer and shadows on every mesh; each surface's named slot bound to this team's material.</summary>
    private static void Bind(Node node, Seat seat)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            mi.Layers = CarNode.CarLayer;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var mat = CarMaterials.For(ModelLibrary.SurfaceSlot(mesh, i), seat);
                if (mat is not null)
                {
                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
            }
        }
        foreach (var c in node.GetChildren())
        {
            Bind(c, seat);
        }
    }

    /// <summary>The in-car helmet (procedural until the per-driver helmet models arrive), centred on the helmet empty.</summary>
    private static ArrayMesh Helmet(Seat seat)
    {
        if (!Helmets.TryGetValue(seat.Driver.Code, out var mesh))
        {
            Helmets[seat.Driver.Code] = mesh = MeshConvert.ToArrayMesh(CarMeshBuilder.BuildHelmet(seat.Team.Livery));
        }
        return mesh;
    }

    // ---- procedural fallback -----------------------------------------------------------------------------

    private static CarNode Procedural(string driverCode)
    {
        var parts = RedlineAssets.Cars[driverCode];
        var g = Convert(driverCode, parts);
        var matte = string.Equals(parts.Seat.Team.Finish, "matte", StringComparison.OrdinalIgnoreCase);
        var car = new CarNode { Name = $"Car_{driverCode}", Code = driverCode };

        Add(car, g.Body, Materials.Paint(matte));
        Add(car, g.Carbon, Materials.Carbon);
        Add(car, g.Halo, Materials.Halo(matte));
        Add(car, g.Lights, Materials.RainLight, shadow: false);
        Add(car, g.Helmet, Materials.Helmet);
        Add(car, g.RimsOther, Materials.Rim);

        var m = parts.Meshes;
        for (var w = 0; w < m.WheelCentres.Count; w++)
        {
            var pivot = new Node3D { Name = $"Wheel{w}", Position = MeshConvert.V(m.WheelCentres[w]) };
            Add(pivot, g.Tyres[w], Materials.Tyre);
            Add(pivot, g.Rims[w], Materials.Rim);
            car.AddWheel(pivot, m.WheelCentres[w].Y);
        }

        AddNumbers(car, parts.Seat, [.. m.NumberDecals.Select((d, i) => DecalTransform(d.Pos, d.Normal, nose: i == 0))],
            [.. m.NumberDecals.Select(d => d.Size)]);
        car.AddChild(ContactShadow());
        car.SetSmoke(new SpinSmoke { Position = new Vector3(0, 0.25f, -1.75f) });
        return car;
    }

    private static GodotCar Convert(string code, CarParts parts)
    {
        if (Meshes.TryGetValue(code, out var g))
        {
            return g;
        }
        var m = parts.Meshes;
        g = new GodotCar(
            MeshConvert.ToArrayMesh(m.Body), MeshConvert.ToArrayMesh(m.Carbon), MeshConvert.ToArrayMesh(m.Halo),
            MeshConvert.ToArrayMesh(m.Lights), MeshConvert.ToArrayMesh(m.Helmet), MeshConvert.ToArrayMesh(parts.RimsOther),
            [.. parts.TyresPerWheel.Select(t => MeshConvert.ToArrayMesh(t))],
            [.. parts.RimsPerWheel.Select(r => MeshConvert.ToArrayMesh(r))]);
        Meshes[code] = g;
        return g;
    }

    private static void Add(Node3D parent, ArrayMesh mesh, Material material, bool shadow = true)
    {
        if (mesh.GetSurfaceCount() == 0)
        {
            return;
        }
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            Layers = CarNode.CarLayer,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ---- shared ------------------------------------------------------------------------------------------

    /// <summary>A decal frame at <paramref name="pos"/> facing <paramref name="normal"/>; text up is -Z (towards the cockpit) on the nose, else +Y.</summary>
    private static Transform3D DecalTransform(System.Numerics.Vector3 pos, System.Numerics.Vector3 normal, bool nose)
    {
        var n = MeshConvert.V(normal).Normalized();
        var up = nose ? new Vector3(0, 0, -1) : Vector3.Up;
        up = (up - n * up.Dot(n)).Normalized();
        return new Transform3D(new Basis(up.Cross(n), up, n), MeshConvert.V(pos));
    }

    /// <summary>
    /// The number on the nose and both rear endplates (<paramref name="at"/>: nose first; each frame's +Z is the outward
    /// normal, +Y the text's up; <paramref name="sizes"/>: glyph heights in metres), in Titillium Web Black (slanted) in the livery's number colour; where that colour would
    /// vanish on the panel behind it, the stronger of black or white is used instead.
    /// </summary>
    private static void AddNumbers(CarNode car, Seat seat, IReadOnlyList<Transform3D> at, IReadOnlyList<float> sizes)
    {
        var livery = seat.Team.Livery;
        for (var i = 0; i < at.Count; i++)
        {
            var nose = i == 0;
            var background = new Color(nose ? livery.Nose : livery.Endplate);
            var colour = Readable(new Color(livery.Number), background, new Color(livery.Top));
            const int fontSize = 128;
            car.AddChild(new Label3D
            {
                Text = seat.Driver.Number.ToString(),
                Font = ArcTheme.BlackItalic,
                FontSize = fontSize,
                OutlineSize = 0,
                // Digit height ≈ 0.7 em in Titillium Web.
                PixelSize = sizes[i] / (fontSize * 0.7f),
                Modulate = colour,
                Shaded = true,
                DoubleSided = false,
                AlphaCut = Label3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Layers = CarNode.CarLayer,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = at[i].TranslatedLocal(new Vector3(0, 0, 0.004f)),
            });
        }
    }

    private static Color Readable(Color wanted, Color background, Color fallback)
    {
        static float Luma(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
        if (Mathf.Abs(Luma(wanted) - Luma(background)) > 0.25f)
        {
            return wanted;
        }
        if (Mathf.Abs(Luma(fallback) - Luma(background)) > 0.25f)
        {
            return fallback;
        }
        return Luma(background) > 0.45f ? new Color("111111") : Colors.White;
    }

    /// <summary>A soft dark ellipse projected on the track under the car, so it sits on the asphalt in every preset.</summary>
    private static Decal ContactShadow()
    {
        if (_contactShadow is null)
        {
            var g = new Gradient();
            g.SetColor(0, new Color(0, 0, 0, 0.85f));
            g.AddPoint(0.55f, new Color(0, 0, 0, 0.5f));
            g.SetColor(g.GetPointCount() - 1, new Color(0, 0, 0, 0));
            _contactShadow = new GradientTexture2D
            {
                Gradient = g,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 64,
                Height = 64,
            };
        }
        return new Decal
        {
            Name = "ContactShadow",
            Size = new Vector3(2.5f, 1.2f, 6.0f),
            Position = new Vector3(0, 0.3f, -0.15f),
            TextureAlbedo = _contactShadow,
            Modulate = new Color(1, 1, 1, 0.8f),
            CullMask = 1,
            UpperFade = 0.2f,
            LowerFade = 0.5f,
        };
    }
}
