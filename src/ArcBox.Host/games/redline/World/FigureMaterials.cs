using Godot;
using Redline.Logic;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Godot materials for the Blender driver figures (<see cref="DriverModel"/>) and helmets (<see cref="HelmetModel"/>).
/// Team slots take the team's colours from grid2026.json; the likeness slots keep the colour the glb was exported with
/// but get a proper surface: skin with subsurface scattering and a faint roughness variation (triplanar noise, the
/// figures carry no UVs), wet eyes, fabric suits, gloss helmet paint with clearcoat and a dark iridium visor.
/// Materials are cached by slot and colour, so every figure and helmet shares them.
/// </summary>
public static class FigureMaterials
{
    private static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);
    private static readonly List<StandardMaterial3D> Skins = [];
    private static bool _subsurface = true;
    /// <summary>Pores and sheen: roughness x 0.8..1.0 in fine blotches.</summary>
    private static readonly Lazy<NoiseTexture2D> SkinGrain = new(() =>
    {
        var t = Materials.Noise(256, 0.09f, 61, FastNoiseLite.NoiseTypeEnum.Value, 3);
        var g = new Gradient();
        g.SetColor(0, new Color(0.8f, 0.8f, 0.8f));
        g.SetColor(g.GetPointCount() - 1, Colors.White);
        t.ColorRamp = g;
        return t;
    });

    /// <summary>The material for a driver figure's <paramref name="slot"/>, or null to keep the imported one.</summary>
    public static Material? Driver(string slot, Material? imported, Livery livery, string accent)
    {
        ArgumentNullException.ThrowIfNull(livery);
        var own = (imported as BaseMaterial3D)?.AlbedoColor ?? new Color("808080");
        var team = DriverModel.TeamColour(slot, livery, accent);
        return slot switch
        {
            DriverModel.SuitPrimary or DriverModel.SuitSecondary or DriverModel.CapSlot or DriverModel.CapBrim =>
                Fabric(team!, 0.72f),
            DriverModel.Belt => Fabric(new Color(team!).Darkened(0.22f).ToHtml(false), 0.55f),
            DriverModel.Gloves => Fabric(team!, 0.62f),
            DriverModel.BottleFoil => Cached($"foil:{team}", () => new StandardMaterial3D
            {
                AlbedoColor = new Color(team!), Metallic = 0.85f, Roughness = 0.28f,
            }),
            DriverModel.BottleGlass => Cached("bottle_glass", () => new StandardMaterial3D
            {
                AlbedoColor = new Color("163a24"),
                Roughness = 0.07f,
                Metallic = 0.15f,
                MetallicSpecular = 0.9f,
                ClearcoatEnabled = true,
                Clearcoat = 1,
                RimEnabled = true,
                Rim = 0.4f,
            }),
            "skin" or "lips" or "stubble" or "stubble_edge" => Skin(own, slot == "lips" ? 0.4f : 0.52f),
            "eyes" or "iris" or "pupil" => Cached($"eye:{own.ToHtml(false)}", () => new StandardMaterial3D
            {
                AlbedoColor = own, Roughness = 0.08f, ClearcoatEnabled = true, Clearcoat = 1, ClearcoatRoughness = 0.02f,
            }),
            "hair" or "brows" or "beard" or "lashes" => Cached($"hair:{own.ToHtml(false)}", () => new StandardMaterial3D
            {
                AlbedoColor = own, Roughness = slot == "hair" ? 0.48f : 0.65f, MetallicSpecular = 0.35f, Rim = 0.25f,
                RimEnabled = true, RimTint = 0.6f,
            }),
            "jewellery" => Cached("jewellery", () => new StandardMaterial3D { AlbedoColor = new Color("eceef2"), Metallic = 1, Roughness = 0.1f }),
            _ => null,
        };
    }

    /// <summary>The material for a helmet's <paramref name="slot"/>, or null to keep the imported one.</summary>
    public static Material? Helmet(string slot, Material? imported)
    {
        var own = (imported as BaseMaterial3D)?.AlbedoColor ?? new Color("ffffff");
        return slot switch
        {
            HelmetModel.Base or HelmetModel.AccentA or HelmetModel.AccentB => Cached($"helmet:{own.ToHtml(false)}", () => new StandardMaterial3D
            {
                AlbedoColor = own,
                Metallic = 0.2f,
                Roughness = 0.24f,
                ClearcoatEnabled = true,
                Clearcoat = 1,
                ClearcoatRoughness = 0.04f,
            }),
            HelmetModel.Visor => Cached("visor", () => new StandardMaterial3D
            {
                AlbedoColor = new Color("15171d"), Metallic = 0.9f, Roughness = 0.05f, MetallicSpecular = 1,
            }),
            _ => null,
        };
    }

    /// <summary>Applies <paramref name="pick"/> to every surface under <paramref name="node"/> (by slot name).</summary>
    public static void Bind(Node node, Func<string, Material?, Material?> pick, uint layers, bool shadows = true)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            mi.Layers = layers;
            mi.CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            for (var i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var mat = pick(ModelLibrary.SurfaceSlot(mesh, i), mesh.SurfaceGetMaterial(i));
                if (mat is not null)
                {
                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
            }
        }
        foreach (var c in node.GetChildren())
        {
            Bind(c, pick, layers, shadows);
        }
    }

    private static StandardMaterial3D Fabric(string hex, float roughness) => Cached($"fabric:{hex}:{roughness}", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(hex),
        Roughness = roughness,
        MetallicSpecular = 0.4f,
        // A little sheen at grazing angles, like race-suit Nomex.
        RimEnabled = true,
        Rim = 0.18f,
        RimTint = 0.7f,
    });

    /// <summary>
    /// Turns the skin's subsurface scattering on or off for every figure, now and later (<see cref="RenderPreset.SkinSubsurface"/>).
    /// It is a screen-space pass: on a software renderer it made the podium warm-up take minutes.
    /// </summary>
    public static void SetSubsurface(bool enabled)
    {
        _subsurface = enabled;
        foreach (var skin in Skins)
        {
            skin.SubsurfScatterEnabled = enabled;
        }
    }

    private static StandardMaterial3D Skin(Color albedo, float roughness) => Cached($"skin:{albedo.ToHtml(false)}:{roughness}", () => Track(new StandardMaterial3D
    {
        AlbedoColor = albedo,
        Roughness = roughness,
        MetallicSpecular = 0.45f,
        RoughnessTexture = SkinGrain.Value,
        RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
        Uv1Triplanar = true,
        Uv1Scale = new Vector3(14, 14, 14),
        SubsurfScatterEnabled = _subsurface,
        SubsurfScatterStrength = 0.35f,
        SubsurfScatterSkinMode = true,
        RimEnabled = true,
        Rim = 0.12f,
        RimTint = 0.8f,
    }));

    private static StandardMaterial3D Track(StandardMaterial3D skin)
    {
        Skins.Add(skin);
        return skin;
    }

    private static T Cached<T>(string key, Func<T> make) where T : Material
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            Cache[key] = m = make();
        }
        return (T)m;
    }
}
