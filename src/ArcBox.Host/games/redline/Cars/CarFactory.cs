using ArcBox.Host.Games.Redline.World;
using ArcBox.Host.Shell;
using Godot;
using Redline.Presentation.Meshes;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// Builds <see cref="CarNode"/>s from the cached procedural meshes (<see cref="RedlineAssets.Cars"/>). Godot meshes are
/// converted once per driver and shared by every scene instance. Livery colours, numbers, T-cams and the matte/gloss
/// finish all come from grid2026.json through <c>Grid2026</c>.
/// </summary>
public static class CarFactory
{
    private sealed record GodotCar(
        ArrayMesh Body, ArrayMesh Carbon, ArrayMesh Halo, ArrayMesh Lights, ArrayMesh Helmet, ArrayMesh RimsOther,
        ArrayMesh[] Tyres, ArrayMesh[] Rims);

    private static readonly Dictionary<string, GodotCar> Meshes = new(StringComparer.Ordinal);
    private static GradientTexture2D? _contactShadow;

    public static CarNode Create(string driverCode)
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

        AddNumbers(car, parts);
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

    /// <summary>
    /// The number on the nose and both rear endplates, in Titillium Web Black (slanted) in the livery's number colour; where
    /// that colour would vanish on the panel behind it, the stronger of black or white is used instead.
    /// </summary>
    private static void AddNumbers(CarNode car, CarParts parts)
    {
        var livery = parts.Seat.Team.Livery;
        var decals = parts.Meshes.NumberDecals;
        for (var i = 0; i < decals.Count; i++)
        {
            var (pos, normal, size) = decals[i];
            var n = MeshConvert.V(normal).Normalized();
            var nose = i == 0;
            var up = nose ? new Vector3(0, 0, -1) : Vector3.Up;
            up = (up - n * up.Dot(n)).Normalized();
            var right = up.Cross(n);
            var background = new Color(nose ? livery.Nose : livery.Endplate);
            var colour = Readable(new Color(livery.Number), background, new Color(livery.Top));
            const int fontSize = 128;
            car.AddChild(new Label3D
            {
                Text = parts.Seat.Driver.Number.ToString(),
                Font = ArcTheme.BlackItalic,
                FontSize = fontSize,
                OutlineSize = 0,
                // Digit height ≈ 0.7 em in Titillium Web.
                PixelSize = size / (fontSize * 0.7f),
                Modulate = colour,
                Shaded = true,
                DoubleSided = false,
                AlphaCut = Label3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                Layers = CarNode.CarLayer,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = new Transform3D(new Basis(right, up, n), MeshConvert.V(pos) + n * 0.004f),
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
