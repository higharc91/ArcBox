using Godot;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// Champagne confetti (podium spec §5): two cannons on top of the backdrop fire a burst of paper over the podium when the
/// champagne begins, and a steady fall drifts down over the stage for the rest of the phase. Each piece is a little
/// card in a podium colour that flutters (its width flips with a per-piece rhythm) and glints as it turns.
/// </summary>
public partial class Confetti : Node3D
{
    private const int CannonPieces = 650;
    private const int RainPieces = 900;

    private static ShaderMaterial? _material;
    private static QuadMesh? _quad;

    private readonly List<GpuParticles3D> _cannons = [];
    private readonly GpuParticles3D _rain;

    /// <param name="cannons">Cannon muzzles in this node's frame, each with the direction it fires.</param>
    /// <param name="rainCentre">Centre of the box the steady fall starts from.</param>
    /// <param name="rainExtents">Half-size of that box.</param>
    public Confetti(IEnumerable<(Vector3 At, Vector3 Aim)> cannons, Vector3 rainCentre, Vector3 rainExtents)
    {
        Name = "Confetti";
        foreach (var (at, aim) in cannons)
        {
            var cannon = Emitter($"Cannon{_cannons.Count + 1}", CannonPieces, 7.5, new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.25f,
                Direction = aim.Normalized(),
                Spread = 22,
                InitialVelocityMin = 9,
                InitialVelocityMax = 17,
                Gravity = new Vector3(0, -2.4f, 0),
                DampingMin = 2.2f,
                DampingMax = 4.2f,
                ScaleMin = 0.09f,
                ScaleMax = 0.15f,
                ColorInitialRamp = Palette(),
                AngleMin = -180,
                AngleMax = 180,
                AngularVelocityMin = -160,
                AngularVelocityMax = 160,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 1.2f,
                TurbulenceNoiseScale = 6,
                TurbulenceInfluenceMin = 0.03f,
                TurbulenceInfluenceMax = 0.09f,
            });
            cannon.OneShot = true;
            cannon.Explosiveness = 0.85f;
            cannon.Position = at;
            _cannons.Add(cannon);
            AddChild(cannon);
        }

        _rain = Emitter("Rain", RainPieces, 7, new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = rainExtents,
            Direction = new Vector3(0, -1, 0),
            Spread = 40,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 1.0f,
            Gravity = new Vector3(0, -1.5f, 0),
            DampingMin = 0.6f,
            DampingMax = 1.2f,
            ScaleMin = 0.09f,
            ScaleMax = 0.14f,
            ColorInitialRamp = Palette(),
            AngleMin = -180,
            AngleMax = 180,
            AngularVelocityMin = -120,
            AngularVelocityMax = 120,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 1.5f,
            TurbulenceNoiseScale = 7,
            TurbulenceInfluenceMin = 0.04f,
            TurbulenceInfluenceMax = 0.1f,
        });
        _rain.Position = rainCentre;
        _rain.Preprocess = 0;
        AddChild(_rain);
    }

    public Confetti()
    {
        _rain = new GpuParticles3D();
    }

    private static GpuParticles3D Emitter(string name, int amount, double lifetime, ParticleProcessMaterial process) => new()
    {
        Name = name,
        Amount = amount,
        Lifetime = lifetime,
        LocalCoords = false,
        Emitting = false,
        FixedFps = 0,
        Interpolate = false,
        ProcessMaterial = process,
        DrawPass1 = Quad,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        VisibilityAabb = new Aabb(new Vector3(-30, -15, -30), new Vector3(60, 45, 60)),
    };

    /// <summary>Fires the cannons and starts the fall.</summary>
    public void Start()
    {
        foreach (var cannon in _cannons)
        {
            cannon.Restart();
            cannon.Emitting = true;
        }
        _rain.Emitting = true;
    }

    /// <summary>Stops the fall (pieces in the air finish) or, with <paramref name="clear"/>, removes every piece at once.</summary>
    public void Stop(bool clear)
    {
        _rain.Emitting = false;
        if (!clear)
        {
            return;
        }
        foreach (var e in _cannons.Append(_rain))
        {
            e.Emitting = false;
            e.Restart();
            e.Emitting = false;
        }
    }

    /// <summary>Red, gold, white, silver and a little black: the REDLINE podium colours.</summary>
    private static GradientTexture1D Palette()
    {
        var g = new Gradient { InterpolationMode = Gradient.InterpolationModeEnum.Constant };
        string[] colours = ["e10600", "ffd400", "ffffff", "c9ced6", "e10600", "f5b800", "ffffff", "1d1d24", "ff3b30", "e8e8e8"];
        g.SetColor(0, new Color(colours[0]));
        g.SetOffset(0, 0);
        g.SetColor(1, new Color(colours[^1]));
        g.SetOffset(1, 1);
        for (var i = 1; i < colours.Length - 1; i++)
        {
            g.AddPoint(i / (float)colours.Length, new Color(colours[i]));
        }
        return new GradientTexture1D { Gradient = g, Width = 64 };
    }

    private static QuadMesh Quad => _quad ??= new QuadMesh { Size = new Vector2(1.0f, 0.62f), Material = Material };

    private static ShaderMaterial Material => _material ??= new ShaderMaterial { Shader = new Shader { Code = FlutterCode } };

    // A particle billboard (as StandardMaterial3D's), whose width flips with each piece's own rhythm: paper tumbling.
    private const string FlutterCode = """
        shader_type spatial;
        render_mode cull_disabled, depth_draw_opaque;

        varying float glint;

        void vertex() {
            mat4 mat_world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
            mat_world = mat_world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
            MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;
            MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
            float seed = float(INSTANCE_ID) * 1.7319;
            float flip = cos(TIME * (4.0 + mod(seed, 5.0)) + seed * 3.1);
            VERTEX.x *= 0.12 + 0.88 * abs(flip);
            glint = pow(abs(flip), 24.0);
            MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
        }

        void fragment() {
            ALBEDO = COLOR.rgb;
            ROUGHNESS = 0.45;
            METALLIC = 0.2;
            EMISSION = COLOR.rgb * (0.18 + 1.6 * glint);
        }
        """;
}
