using Godot;
using Environment = Godot.Environment;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>The built world: the root node plus the pieces the scene drives later.</summary>
public sealed record BuiltWorld(Node3D Root, WorldEnvironment Environment, DirectionalLight3D Sun, StartGantry Gantry, Node3D Trees);

/// <summary>
/// Assembles the static COTA world from <see cref="RedlineAssets"/>: sky and environment, the sun, track layers, terrain,
/// landmarks, trees, the surroundings (<see cref="Surroundings"/>) and the barriers and trackside furniture (<see cref="Trackside"/>). Godot meshes for the track and terrain are converted
/// once and cached for later scene instances.
/// </summary>
public static class WorldBuilder
{
    /// <summary>Sun height above the horizon and compass bearing it shines from (degrees; 0 = north, 90 = east).</summary>
    public const float SunElevation = 48;
    public const float SunAzimuth = 25;

    private static readonly Dictionary<string, ArrayMesh[]> TrackCache = new(StringComparer.Ordinal);

    public static BuiltWorld Build(CircuitAssets a, Preset preset)
    {
        var root = new Node3D { Name = "World" };

        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = Materials.Sky, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Environment.AmbientSource.Sky,
            // Part sky, part warm neutral: shade under the pit building should read grey, not blue.
            AmbientLightSkyContribution = 0.65f,
            AmbientLightColor = new Color("8d8a80"),
            AmbientLightEnergy = 1.0f,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Agx,
            TonemapExposure = 1.0f,
            TonemapAgxWhite = 8f,
            TonemapAgxContrast = 1.45f,
            FogEnabled = true,
            FogMode = Environment.FogModeEnum.Exponential,
            FogLightColor = new Color("c9d6e2"),
            FogDensity = 0.00022f,
            FogAerialPerspective = 0.55f,
            FogSkyAffect = 0.0f,
            FogSunScatter = 0.08f,
            GlowEnabled = true,
            GlowIntensity = 0.35f,
            GlowHdrThreshold = 1.4f,
            GlowBloom = 0.02f,
        };
        var worldEnv = new WorldEnvironment { Environment = env, Name = "WorldEnvironment" };
        root.AddChild(worldEnv);

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = new Color("fff1dc"),
            LightEnergy = 2.4f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 420,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowFadeStart = 0.85f,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.2f,
            ShadowBlur = 1.2f,
            LightAngularDistance = 0.6f,
        };
        root.AddChild(sun);
        sun.Basis = Basis.FromEuler(new Vector3(-Mathf.DegToRad(SunElevation), Mathf.DegToRad(180 - SunAzimuth), 0));

        var meshes = TrackMeshes(a);
        (string Name, Material Material, bool Shadow)[] layers =
        [
            ("Asphalt", TrackMaterials.Asphalt, false),
            ("Lines", TrackMaterials.Paint, false),
            ("Curbs", TrackMaterials.Kerbs, false),
            ("Runoff", TrackMaterials.Runoff, false),
            ("Walls", Materials.Walls, true),
            ("Fence", Materials.Fence, false),
            ("Verge", TrackMaterials.Verge, false),
            ("Terrain", Materials.Terrain, false),
            ("Gravel", TrackMaterials.Gravel, false),
        ];
        for (var i = 0; i < layers.Length; i++)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = layers[i].Name,
                Mesh = meshes[i],
                MaterialOverride = layers[i].Material,
                CastShadow = layers[i].Shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        AdBoards.Apply(root, Materials.Walls);

        var landmarks = Landmarks.Build(a, out var gantry);
        root.AddChild(landmarks);
        var footprints = Landmarks.Footprints(a);
        var trees = Trees.Build(a, footprints);
        root.AddChild(Surroundings.Build(a, footprints, trees, root.GetNode<MeshInstance3D>("Terrain")));
        Surroundings.SetDensity(root, preset);
        root.AddChild(Trackside.Build(a, root.GetNode<MeshInstance3D>("Fence")));
        Trackside.SetPreset(root, preset);
        Trees.SetFraction(trees, RenderPreset.TreeFraction(preset));
        root.AddChild(trees);

        return new BuiltWorld(root, worldEnv, sun, gantry, trees);
    }

    private static ArrayMesh[] TrackMeshes(CircuitAssets a)
    {
        var key = a.Circuit.Name;
        if (TrackCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var t = a.Track;
        var meshes = new[] { t.Asphalt, t.Lines, t.Curbs, t.Runoff, t.Walls, t.Fence, t.Grass, a.Terrain, t.Gravel }
            .Select(m => MeshConvert.ToArrayMesh(m))
            .ToArray();
        TrackCache[key] = meshes;
        return meshes;
    }
}
