using Godot;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// The Godot materials bound to the named slots of the Blender podium set and landmarks (<c>PodiumModel</c>,
/// <c>LandmarkModels</c>): board-marked concrete, painted and dark steel, brushed metal, tinted and clear glass, seat
/// plastic, roller-shutter aluminium, LED strips, the start gantry's dot-matrix board, bark and leaves. A grandstand's
/// <c>roof</c>, <c>trim</c> and <c>brand</c> slots take the stand's colour (<paramref name="tint"/>). Textures are
/// generated here, deterministically; materials are cached and shared. Unknown slots keep the glb's own material.
/// </summary>
public static class SetMaterials
{
    private static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);

    private static readonly Lazy<NoiseTexture2D> ConcreteGrain = new(() => Grain(41, 0.04f, new Color(0.86f, 0.86f, 0.85f), Colors.White));
    private static readonly Lazy<NoiseTexture2D> LeafGrain = new(() => Grain(57, 0.12f, new Color(0.72f, 0.78f, 0.7f), new Color(1.06f, 1.04f, 0.98f)));
    private static readonly Lazy<ImageTexture> Dots = new(MakeDots);

    /// <summary>The material for <paramref name="slot"/>, or null to keep the imported one.</summary>
    public static Material? For(string slot, Color? tint = null) => slot switch
    {
        "concrete" => Concrete("concrete", "c4c1b9"),
        "concrete_dark" => Concrete("concrete_dark", "9c998f"),
        "tower_white" => Cached("tower_white", () => new StandardMaterial3D { AlbedoColor = new Color("eef0f2"), Roughness = 0.5f }),
        "seat" => Cached("seat", () => new StandardMaterial3D
        {
            // Dark slate plastic: empty seats read as seats, and every crowd colour stands out against them.
            AlbedoColor = new Color("33405a"),
            Roughness = 0.34f,
            MetallicSpecular = 0.55f,
            ClearcoatEnabled = true,
            Clearcoat = 0.4f,
            ClearcoatRoughness = 0.2f,
        }),
        "safety" => Plain("safety", "e8c21a", 0.6f),
        "glass" => Cached("glass", () => new StandardMaterial3D
        {
            AlbedoColor = new Color("1b2a3f"),
            Metallic = 0.75f,
            Roughness = 0.05f,
            MetallicSpecular = 0.8f,
        }),
        "glass_clear" => Cached("glass_clear", () => new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.72f, 0.8f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.04f,
            Metallic = 0.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        }),
        "metal" => Cached("metal", () => new StandardMaterial3D { AlbedoColor = new Color("a6acb4"), Metallic = 1, Roughness = 0.34f, AnisotropyEnabled = true, Anisotropy = 0.3f }),
        "steel" => Plain("steel", "dcdfe3", 0.4f, 0.35f),
        "steel_dark" => Plain("steel_dark", "383b42", 0.42f, 0.5f),
        "roof" => Plain($"roof:{Hex(tint)}", Hex(tint) ?? "eeeff1", 0.55f, 0.1f),
        "trim" => Plain($"trim:{Hex(tint)}", (tint ?? new Color("eeeff1")).Darkened(0.12f).ToHtml(false), 0.4f, 0.2f),
        "brand" => Gloss($"brand:{Hex(tint)}", BrandOn(tint)),
        "shutter" => Cached("shutter", () => new StandardMaterial3D { AlbedoColor = new Color("c6cacf"), Metallic = 0.75f, Roughness = 0.4f, AnisotropyEnabled = true, Anisotropy = 0.5f }),
        "interior" => Plain("interior", "24262b", 0.8f),
        "wall_white" => Plain("wall_white", "e3e6ea", 0.5f),
        "epoxy" => Plain("epoxy", "8a8d92", 0.45f),
        "monitor" => Materials.Emissive("3a5f8f", 1.2f),
        "panel" => Gloss("panel", "15161b"),
        "red" => Gloss("red", "d0101c"),
        "board" => Cached("board", () => new StandardMaterial3D
        {
            // Dot-matrix LED board: UVs are in decimetres, one dot per texture repeat.
            AlbedoColor = new Color("0b0c10"),
            Roughness = 0.35f,
            EmissionEnabled = true,
            Emission = new Color("ffb347"),
            EmissionEnergyMultiplier = 0.12f,
            EmissionTexture = Dots.Value,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        }),
        "lamp" => Plain("lamp", "2a0606", 0.25f),
        "led_white" => Materials.Emissive("f4f7ff", 3),
        "led_red" => Materials.Emissive("ff1a12", 4),
        "led_warm" => Materials.Emissive("ffe7c4", 6),
        "bark" => Plain("bark", "4a3b2c", 0.95f),
        "leaves" => Cached("leaves", () => new StandardMaterial3D
        {
            // White under the MultiMesh's per-instance green (the scatter's shade), with a leafy mottle.
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            AlbedoTexture = LeafGrain.Value,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(2.5f, 2.5f, 2.5f),
            Roughness = 0.92f,
            MetallicSpecular = 0.2f,
            BacklightEnabled = true,
            Backlight = new Color(0.18f, 0.22f, 0.08f),
        }),
        // The podium set.
        "stage" => Cached("stage", () => new StandardMaterial3D { AlbedoColor = new Color("1c1d23"), Metallic = 0.3f, Roughness = 0.42f, ClearcoatEnabled = true, Clearcoat = 0.4f }),
        "carpet" => Plain("carpet", "2b2c34", 0.95f),
        "step" => Cached("step", () => new StandardMaterial3D { AlbedoColor = new Color("eef0f3"), Roughness = 0.3f, ClearcoatEnabled = true, Clearcoat = 0.5f, ClearcoatRoughness = 0.08f }),
        "gold" => Plain("gold", "d4a52c", 0.22f, 1),
        "silver" => Plain("silver", "c3c8cf", 0.2f, 1),
        "bronze" => Plain("bronze", "b8733a", 0.26f, 1),
        "numeral" => Gloss("numeral", "e10600"),
        "brand_red" => Gloss("brand_red", "e10600"),
        "brand_white" => Gloss("brand_white", "f4f4f4"),
        "cannon" => Gloss("cannon", "c8102e"),
        _ => null,
    };

    /// <summary>Binds every surface under <paramref name="node"/> to its slot's material; shadows on.</summary>
    public static void Bind(Node node, Color? tint = null, Func<string, Material?>? special = null)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var slot = ModelLibrary.SurfaceSlot(mesh, i);
                var mat = special?.Invoke(slot) ?? For(slot, tint);
                if (mat is not null)
                {
                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
            }
        }
        foreach (var c in node.GetChildren())
        {
            Bind(c, tint, special);
        }
    }

    private static string? Hex(Color? c) => c?.ToHtml(false);

    /// <summary>Lettering on a stand's fascia: red on a light roof, white on a coloured one.</summary>
    private static string BrandOn(Color? tint) => tint is { } c && c.Luminance > 0.6f ? "e10600" : "f4f4f4";

    private static T Cached<T>(string key, Func<T> make) where T : Material
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            Cache[key] = m = make();
        }
        return (T)m;
    }

    private static StandardMaterial3D Plain(string key, string hex, float roughness, float metallic = 0) =>
        Cached(key, () => new StandardMaterial3D { AlbedoColor = new Color(hex), Roughness = roughness, Metallic = metallic });

    private static StandardMaterial3D Gloss(string key, string hex) =>
        Cached(key, () => new StandardMaterial3D { AlbedoColor = new Color(hex), Roughness = 0.3f, Metallic = 0.1f, ClearcoatEnabled = true, Clearcoat = 0.6f });

    /// <summary>Cast concrete: a soft mottle (triplanar, world space so long walls don't stretch it).</summary>
    private static StandardMaterial3D Concrete(string key, string hex) => Cached(key, () => new StandardMaterial3D
    {
        AlbedoColor = new Color(hex),
        AlbedoTexture = ConcreteGrain.Value,
        Uv1Triplanar = true,
        Uv1WorldTriplanar = true,
        Uv1Scale = new Vector3(0.18f, 0.18f, 0.18f),
        Roughness = 0.9f,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
    });

    private static NoiseTexture2D Grain(int seed, float frequency, Color low, Color high)
    {
        var tex = Materials.Noise(256, frequency, seed);
        tex.ColorRamp = new Gradient { Colors = [low, high], Offsets = [0, 1] };
        return tex;
    }

    /// <summary>One round LED per texture repeat: a soft dot on black.</summary>
    private static ImageTexture MakeDots()
    {
        const int size = 16;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2(x + 0.5f - (size / 2f), y + 0.5f - (size / 2f)).Length() / (size * 0.36f);
                var v = Mathf.Clamp(1.2f - d, 0, 1);
                img.SetPixel(x, y, new Color(v * v, v * v, v * v));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
