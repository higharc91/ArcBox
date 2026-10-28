using Godot;
using Redline.Logic;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// The Godot materials bound to the Blender car's named slots (<see cref="CarModel"/>). Livery slots take the team's
/// colours from grid2026.json: <c>new Color("#rrggbb")</c> is sRGB and <see cref="BaseMaterial3D.AlbedoColor"/> is an
/// sRGB colour, so the hex goes in unchanged (papaya stays papaya; the glb's own linear base colours are not used).
/// Gloss paint is metallic flake under a clearcoat (the flake normal map tilts the base layer only; Godot's clearcoat
/// uses the geometric normal), matte paint has no coat. Carbon has a twill weave and a satin finish (no coat: a lacquer
/// mirrors the bright sky off the floor at grazing angles), rims are brushed (anisotropic) metal.
/// All textures are generated here, deterministically: no image assets. Materials are cached and shared.
/// </summary>
public static class CarMaterials
{
    private static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);
    private static readonly Lazy<ImageTexture> Flakes = new(MakeFlakes);
    private static readonly Lazy<(ImageTexture Albedo, ImageTexture Normal)> Weave = new(MakeWeave);

    /// <summary>The material for <paramref name="slot"/> on <paramref name="seat"/>'s car, or null for an unknown slot.</summary>
    public static Material? For(string slot, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var matte = string.Equals(seat.Team.Finish, "matte", StringComparison.OrdinalIgnoreCase);
        var hex = CarModel.LiveryColour(slot, seat.Team.Livery);
        if (hex is not null)
        {
            return Paint(hex, matte);
        }
        return slot switch
        {
            CarModel.Carbon => Carbon,
            CarModel.Tyre => Tyre,
            CarModel.TyreStripe => Cached("tyre_stripe", () => new StandardMaterial3D { AlbedoColor = new Color("f2c200"), Roughness = 0.55f }),
            CarModel.Rim => Rim,
            CarModel.Light => Cached("light", () => new StandardMaterial3D
            {
                AlbedoColor = new Color("ff1a12"),
                EmissionEnabled = true,
                Emission = new Color("ff1a12"),
                EmissionEnergyMultiplier = 2.5f,
                Roughness = 0.3f,
            }),
            CarModel.Glass => Cached("glass", () => new StandardMaterial3D { AlbedoColor = new Color("c4ced8"), Metallic = 1, Roughness = 0.04f }),
            CarModel.Tcam => Tcam(seat.Driver.Tcam),
            _ => null,
        };
    }

    private static T Cached<T>(string key, Func<T> make) where T : Material
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            Cache[key] = m = make();
        }
        return (T)m;
    }

    public static StandardMaterial3D Paint(string hex, bool matte) => Cached($"paint:{hex}:{matte}", () =>
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = new Color(hex),
            Metallic = matte ? 0.2f : 0.3f,
            Roughness = matte ? 0.44f : 0.28f,
            MetallicSpecular = 0.5f,
            NormalEnabled = true,
            NormalTexture = Flakes.Value,
            NormalScale = matte ? 0.08f : 0.2f,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(6, 6, 6),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (!matte)
        {
            m.ClearcoatEnabled = true;
            m.Clearcoat = 1;
            m.ClearcoatRoughness = 0.05f;
        }
        return m;
    });

    public static StandardMaterial3D Carbon => Cached("carbon", () =>
    {
        var (albedo, normal) = Weave.Value;
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = albedo,
            Metallic = 0.15f,
            Roughness = 0.46f,
            MetallicSpecular = 0.35f,
            NormalEnabled = true,
            NormalTexture = normal,
            NormalScale = 0.6f,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(30, 30, 30),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    });

    public static StandardMaterial3D Tyre => Cached("tyre", () => new StandardMaterial3D
    {
        AlbedoColor = new Color("18181b"),
        Roughness = 0.84f,
        MetallicSpecular = 0.35f,
    });

    /// <summary>Brushed gunmetal: anisotropic highlights along the lathe's circumferential UVs (tangents from the import).</summary>
    public static StandardMaterial3D Rim => Cached("rim", () => new StandardMaterial3D
    {
        AlbedoColor = new Color("767a82"),
        Metallic = 1,
        Roughness = 0.3f,
        AnisotropyEnabled = true,
        Anisotropy = 0.5f,
    });

    private static StandardMaterial3D Tcam(string tcam) =>
        Cached($"tcam:{tcam}", () => new StandardMaterial3D
        {
            AlbedoColor = new Color(string.Equals(tcam, "yellow", StringComparison.OrdinalIgnoreCase) ? "f6e600" : "101012"),
            Roughness = 0.25f,
            ClearcoatEnabled = true,
            Clearcoat = 1,
        });

    /// <summary>Metallic flakes: every texel a randomly tilted normal (up to ~20°); mipmaps average them out with distance.</summary>
    private static ImageTexture MakeFlakes()
    {
        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(20261028);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = (float)(rng.NextDouble() * 2 - 1) * 0.36f;
                var ny = (float)(rng.NextDouble() * 2 - 1) * 0.36f;
                var n = new Vector3(nx, ny, 1).Normalized();
                img.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>2x2 twill carbon: 4x4 tows per tile, warp and weft alternating in diagonal steps.</summary>
    private static (ImageTexture, ImageTexture) MakeWeave()
    {
        const int size = 128, tows = 4, tow = size / tows;
        var albedo = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var bump = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int i = x / tow, j = y / tow;
                var warp = ((i + j) & 3) < 2;
                var across = (warp ? y % tow : x % tow) / (float)tow;
                var along = (warp ? x : y) / (float)tow;
                var h = Mathf.Sin(Mathf.Pi * across) * (0.85f + 0.15f * Mathf.Cos(Mathf.Pi * 2 * along / 2));
                var fibre = 0.9f + 0.1f * Mathf.Sin((warp ? y : x) * 2.1f);
                var v = (warp ? 0.095f : 0.06f) * (0.55f + 0.45f * h) * fibre;
                albedo.SetPixel(x, y, new Color(v, v, v * 1.08f));
                bump.SetPixel(x, y, new Color(h, h, h));
            }
        }
        bump.BumpMapToNormalMap(4f);
        albedo.GenerateMipmaps();
        bump.GenerateMipmaps();
        return (ImageTexture.CreateFromImage(albedo), ImageTexture.CreateFromImage(bump));
    }
}
