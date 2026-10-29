using Godot;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// Shared textures, meshes and particle settings for the champagne and the confetti, built once per process. Process
/// materials are per emitter (each spray sets its own speeds); textures and draw meshes are shared.
/// </summary>
public static class ChampagneFx
{
    private static readonly Color FoamGold = new("f7dc8c");
    private static readonly Color FoamWhite = new("fff6dd");

    private static ImageTexture? _foamTexture;
    private static ImageTexture? _starTexture;
    private static QuadMesh? _foamQuad;
    private static QuadMesh? _dropletQuad;
    private static QuadMesh? _mistQuad;
    private static QuadMesh? _burstQuad;
    private static Mesh? _cork;

    /// <summary>A foamy blob: a soft disc made of a cluster of bubbles with bright rims.</summary>
    public static ImageTexture FoamTexture => _foamTexture ??= MakeFoam();

    /// <summary>A four-pointed glint with a soft core, for droplets and the pop.</summary>
    public static ImageTexture StarTexture => _starTexture ??= MakeStar();

    private static ImageTexture MakeFoam()
    {
        const int n = 96;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 1234 };
        var bubbles = new List<(float X, float Y, float R)>();
        for (var i = 0; i < 26; i++)
        {
            var a = rng.Randf() * Mathf.Tau;
            var d = Mathf.Sqrt(rng.Randf()) * 0.62f;
            bubbles.Add((0.5f + (Mathf.Cos(a) * d * 0.5f), 0.5f + (Mathf.Sin(a) * d * 0.5f), rng.RandfRange(0.05f, 0.16f)));
        }
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var u = (x + 0.5f) / n;
                var v = (y + 0.5f) / n;
                var r = new Vector2(u - 0.5f, v - 0.5f).Length() * 2;
                // A soft, misty body; the bubbles only shade it a little, so overlapping blobs merge into one plume.
                var body = Mathf.Pow(Mathf.Clamp(1 - r, 0, 1), 1.4f);
                float rim = 0, fill = 0;
                foreach (var (bx, by, br) in bubbles)
                {
                    var d = new Vector2(u - bx, v - by).Length() / br;
                    if (d < 1)
                    {
                        fill = Mathf.Max(fill, 1 - (d * d));
                        rim = Mathf.Max(rim, Mathf.SmoothStep(0.7f, 0.95f, d) * (1 - Mathf.SmoothStep(0.95f, 1f, d)));
                    }
                }
                var alpha = body * (0.8f + (0.2f * fill));
                var shade = 0.86f + (0.14f * rim) - (0.05f * r);
                image.SetPixel(x, y, new Color(shade, shade, shade, Mathf.Clamp(alpha, 0, 1)));
            }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture MakeStar()
    {
        const int n = 64;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var u = ((x + 0.5f) / n * 2) - 1;
                var v = ((y + 0.5f) / n * 2) - 1;
                var r = Mathf.Sqrt((u * u) + (v * v));
                var core = Mathf.Exp(-r * r * 18);
                var rays = (Mathf.Exp(-Mathf.Abs(u) * 30) + Mathf.Exp(-Mathf.Abs(v) * 30)) * Mathf.Clamp(1 - r, 0, 1);
                var a = Mathf.Clamp(core + (rays * 0.8f) + (Mathf.Exp(-r * r * 4) * 0.25f), 0, 1);
                image.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static StandardMaterial3D Billboard(Texture2D texture, float emission, bool additive = false) => new()
    {
        AlbedoTexture = texture,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        BillboardKeepScale = true,
        Roughness = 0.35f,
        MetallicSpecular = 0.8f,
        EmissionEnabled = emission > 0,
        Emission = new Color(1, 0.93f, 0.72f),
        EmissionEnergyMultiplier = emission,
        EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Add,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        DisableReceiveShadows = true,
        ProximityFadeEnabled = true,
        ProximityFadeDistance = 0.25f,
    };

    /// <summary>
    /// Foam blobs: unshaded, so the jet stays bright golden-white however the sun falls on its billboards (the colour
    /// ramp is HDR, a touch over white, so the brightest foam catches the glow).
    /// </summary>
    public static QuadMesh FoamQuad => _foamQuad ??= new QuadMesh { Size = Vector2.One, Material = Foam() };

    private static StandardMaterial3D Foam()
    {
        var m = Billboard(FoamTexture, 0);
        m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return m;
    }

    /// <summary>Droplets: tiny additive glints bright enough to catch the glow, so the jet sparkles.</summary>
    public static QuadMesh DropletQuad => _dropletQuad ??= new QuadMesh
    {
        Size = Vector2.One,
        Material = new StandardMaterial3D
        {
            AlbedoTexture = StarTexture,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.9f, 1.7f, 1.2f),
        },
    };

    /// <summary>The haze at the mouth: big, faint, soft.</summary>
    public static QuadMesh MistQuad => _mistQuad ??= new QuadMesh { Size = Vector2.One, Material = Mist() };

    private static StandardMaterial3D Mist()
    {
        var soft = new Gradient();
        soft.SetColor(0, new Color(1, 1, 1, 1));
        soft.SetColor(1, new Color(1, 1, 1, 0));
        var m = Billboard(new GradientTexture2D
        {
            Gradient = soft,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1, 0.5f),
            Width = 64,
            Height = 64,
        }, 0);
        m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        return m;
    }

    public static QuadMesh BurstQuad => _burstQuad ??= new QuadMesh { Size = Vector2.One };

    /// <summary>A fresh material for one pop burst (its alpha is animated per spray).</summary>
    public static StandardMaterial3D BurstMaterial() => new()
    {
        AlbedoTexture = StarTexture,
        AlbedoColor = new Color(1, 0.96f, 0.8f, 0),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        Emission = new Color(1, 0.92f, 0.7f),
        EmissionEnergyMultiplier = 4,
        NoDepthTest = false,
    };

    /// <summary>The cork: a squat mushroom of pale cork under a gold cap.</summary>
    public static Mesh CorkMesh => _cork ??= MakeCork();

    private static Mesh MakeCork()
    {
        var p = new World.Prims();
        p.Cylinder(new Vector3(0, -0.022f, 0), new Vector3(0, 0.012f, 0), 0.0135f, 0.0145f, 12, new Color("c8a774"));
        p.Ellipsoid(new Vector3(0, 0.016f, 0), new Vector3(0.019f, 0.014f, 0.019f), 12, 6, new Color("d9b45a"));
        var mesh = p.Commit();
        mesh.SurfaceSetMaterial(0, World.Materials.VertexColour(0.6f, 0.2f, "cork"));
        return mesh;
    }

    private static GradientTexture1D Ramp(params (float At, Color Colour)[] stops)
    {
        var g = new Gradient();
        g.SetColor(0, stops[0].Colour);
        g.SetOffset(0, stops[0].At);
        g.SetColor(1, stops[^1].Colour);
        g.SetOffset(1, stops[^1].At);
        foreach (var (at, colour) in stops.Skip(1).SkipLast(1))
        {
            g.AddPoint(at, colour);
        }
        return new GradientTexture1D { Gradient = g, UseHdr = true };
    }

    private static CurveTexture Curve(params (float X, float Y)[] points)
    {
        var c = new Curve { MaxValue = 2 };
        foreach (var (x, y) in points)
        {
            c.AddPoint(new Vector2(x, y));
        }
        return new CurveTexture { Curve = c };
    }

    /// <summary>The jet: thrown along +Y of the emitter, falling under gravity, swelling and fading as it breaks up.</summary>
    public static ParticleProcessMaterial FoamProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.02f,
        Direction = new Vector3(0, 1, 0),
        Gravity = new Vector3(0, -9.81f, 0),
        DampingMin = 0.6f,
        DampingMax = 1.6f,
        ScaleCurve = Curve((0, 0.15f), (0.22f, 0.75f), (1, 1.8f)),
        ColorRamp = Ramp((0, new Color(1.8f, 1.7f, 1.35f, 0.95f)), (0.2f, new Color(1.7f, 1.35f, 0.62f, 0.85f)), (0.65f, new Color(1.45f, 1.18f, 0.6f, 0.62f)), (1, new Color(1.3f, 1.2f, 0.95f, 0))),
        HueVariationMin = -0.02f,
        HueVariationMax = 0.02f,
        AngleMin = -180,
        AngleMax = 180,
        AngularVelocityMin = -90,
        AngularVelocityMax = 90,
    };

    /// <summary>Sparkling droplets: faster, wider and smaller than the foam, twinkling as they fly.</summary>
    public static ParticleProcessMaterial DropletProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.02f,
        Direction = new Vector3(0, 1, 0),
        Gravity = new Vector3(0, -9.81f, 0),
        DampingMin = 0.2f,
        DampingMax = 0.6f,
        ScaleMin = 0.035f,
        ScaleMax = 0.065f,
        ColorRamp = Ramp((0, new Color(1, 0.95f, 0.75f, 1)), (0.3f, new Color(1, 0.85f, 0.45f, 0.9f)), (0.55f, new Color(1, 0.97f, 0.8f, 1)), (1, new Color(1, 0.85f, 0.5f, 0))),
        AngleMin = -180,
        AngleMax = 180,
    };

    /// <summary>A short-lived haze puffing out of the mouth.</summary>
    public static ParticleProcessMaterial MistProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.05f,
        Direction = new Vector3(0, 1, 0),
        Spread = 35,
        InitialVelocityMin = 0.3f,
        InitialVelocityMax = 2.5f,
        Gravity = new Vector3(0, -0.6f, 0),
        DampingMin = 1.5f,
        DampingMax = 3,
        ScaleMin = 0.2f,
        ScaleMax = 1.0f,
        ScaleCurve = Curve((0, 0.4f), (1, 1.5f)),
        ColorRamp = Ramp((0, new Color(1, 0.97f, 0.88f, 0)), (0.15f, new Color(1, 0.95f, 0.85f, 0.32f)), (1, new Color(1, 0.95f, 0.85f, 0))),
        AngleMin = -180,
        AngleMax = 180,
    };

    /// <summary>Foam dripping off the bottle's mouth after the jet.</summary>
    public static ParticleProcessMaterial DripProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 0.03f,
        Direction = new Vector3(0, -1, 0),
        Spread = 25,
        InitialVelocityMin = 0.1f,
        InitialVelocityMax = 0.5f,
        Gravity = new Vector3(0, -6f, 0),
        ScaleMin = 0.035f,
        ScaleMax = 0.07f,
        ColorRamp = Ramp((0, new Color(FoamWhite, 0.95f)), (0.8f, new Color(FoamGold, 0.85f)), (1, new Color(FoamGold, 0))),
    };
}
