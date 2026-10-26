using Godot;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// Tyre smoke for a spin-out: world-space billboard puffs from the rear tyres that billow up, spread and fade. The
/// emission rate follows <see cref="Intensity"/> (0..1), driven by <c>RevealMotion</c>.
/// </summary>
public partial class SpinSmoke : GpuParticles3D
{
    private static ParticleProcessMaterial? _process;
    private static QuadMesh? _quad;

    public SpinSmoke()
    {
        Name = "SpinSmoke";
        Amount = 260;
        Lifetime = 3.4;
        LocalCoords = false;
        Emitting = false;
        // Simulate with the frame delta, so --fixed-fps captures match real time.
        FixedFps = 0;
        Interpolate = false;
        VisibilityAabb = new Aabb(new Vector3(-30, -2, -30), new Vector3(60, 25, 60));
        CastShadow = ShadowCastingSetting.Off;
        ProcessMaterial = Process();
        DrawPass1 = Quad();
    }

    /// <summary>0 stops new puffs (existing ones finish); 1 is full smoke.</summary>
    public float Intensity
    {
        get => AmountRatio;
        set
        {
            AmountRatio = Mathf.Clamp(value, 0, 1);
            var on = value > 0.01f;
            if (on != Emitting)
            {
                Emitting = on;
            }
        }
    }

    private static ParticleProcessMaterial Process()
    {
        if (_process is not null)
        {
            return _process;
        }
        var alpha = new Gradient();
        alpha.SetColor(0, new Color(0.93f, 0.93f, 0.94f, 0));
        alpha.AddPoint(0.06f, new Color(0.95f, 0.95f, 0.96f, 0.85f));
        alpha.AddPoint(0.5f, new Color(0.92f, 0.92f, 0.94f, 0.5f));
        alpha.SetColor(alpha.GetPointCount() - 1, new Color(0.88f, 0.88f, 0.9f, 0));
        var grow = new Curve();
        grow.AddPoint(new Vector2(0, 0.35f));
        grow.AddPoint(new Vector2(0.4f, 0.8f));
        grow.AddPoint(new Vector2(1, 1));
        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(1.0f, 0.2f, 0.8f),
            Direction = new Vector3(0, 1, 0),
            Spread = 75,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 3.5f,
            Gravity = new Vector3(0, 0.7f, 0),
            DampingMin = 1.2f,
            DampingMax = 2.2f,
            ScaleMin = 3.2f,
            ScaleMax = 6.0f,
            ScaleCurve = new CurveTexture { Curve = grow },
            ColorRamp = new GradientTexture1D { Gradient = alpha },
            AngleMin = -180,
            AngleMax = 180,
            AngularVelocityMin = -25,
            AngularVelocityMax = 25,
        };
        return _process;
    }

    private static QuadMesh Quad()
    {
        if (_quad is not null)
        {
            return _quad;
        }
        var soft = new Gradient();
        soft.SetColor(0, new Color(1, 1, 1, 1));
        soft.AddPoint(0.45f, new Color(1, 1, 1, 0.55f));
        soft.SetColor(soft.GetPointCount() - 1, new Color(1, 1, 1, 0));
        var puff = new GradientTexture2D
        {
            Gradient = soft,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 128,
            Height = 128,
        };
        _quad = new QuadMesh
        {
            Size = new Vector2(1, 1),
            Material = new StandardMaterial3D
            {
                AlbedoTexture = puff,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                BillboardKeepScale = true,
                Roughness = 1,
                // A little self-illumination keeps tyre smoke white rather than sky-blue in shade.
                EmissionEnabled = true,
                Emission = new Color(0.55f, 0.55f, 0.56f),
                EmissionEnergyMultiplier = 0.6f,
                DisableReceiveShadows = false,
                ProximityFadeEnabled = true,
                ProximityFadeDistance = 0.8f,
            },
        };
        return _quad;
    }
}
