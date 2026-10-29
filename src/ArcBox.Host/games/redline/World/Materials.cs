using Godot;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Every material in the REDLINE scene. Track surfaces use small spatial shaders so asphalt and grass get multi-scale
/// procedural texture (no image assets); cars use <see cref="StandardMaterial3D"/> with sRGB vertex colours for the livery.
/// Materials are cached statically and shared between scene instances.
/// </summary>
public static class Materials
{
    private const string SrgbToLinear = """
        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }
        """;

    /// <summary>Asphalt and painted asphalt (UV = metres along the lap, metres left of centre).</summary>
    private const string AsphaltCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D grain : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D blotch : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform float grain_strength = 1.0;
        uniform float racing_line = 0.0;
        uniform float roughness_base = 0.9;
        uniform float brightness = 1.0;
        varying vec3 world_pos;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb) * brightness;
            float g = texture(grain, UV * 0.25).r;
            float g2 = texture(grain, UV * 0.93 + vec2(0.37, 0.11)).r;
            float b = texture(blotch, world_pos.xz * 0.011).r;
            float b2 = texture(blotch, world_pos.xz * 0.047 + vec2(0.5)).r;
            float line = exp(-pow(UV.y / 2.8, 2.0)) * racing_line;
            float shade = mix(1.0 - 0.28 * grain_strength, 1.0 + 0.22 * grain_strength, g)
                * mix(1.0 - 0.12 * grain_strength, 1.0 + 0.1 * grain_strength, g2)
                * mix(0.9, 1.08, b) * mix(0.95, 1.04, b2)
                * (1.0 - 0.2 * line);
            ALBEDO = base * shade;
            ROUGHNESS = clamp(roughness_base - 0.1 * g2 * grain_strength - 0.12 * line, 0.3, 1.0);
            SPECULAR = 0.45;
        }
        """;

    /// <summary>Grass verge and terrain: vertex colour broken up by noise at three scales, dry patches, mown stripes.</summary>
    private const string GrassCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D big : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float stripes = 0.0;
        varying vec3 world_pos;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            float n1 = texture(big, world_pos.xz * 0.0021).r;
            float n0 = texture(big, world_pos.xz * 0.0105 + vec2(0.3, 0.7)).r;
            float n2 = texture(fine, world_pos.xz * 0.11).r;
            float n3 = texture(fine, world_pos.xz * 0.83 + vec2(0.21, 0.4)).r;
            vec3 c = base * mix(0.7, 1.2, n1) * mix(0.86, 1.1, n0) * mix(0.88, 1.1, n2) * mix(0.9, 1.07, n3);
            float dry = smoothstep(0.55, 0.8, n1 * 0.7 + n0 * 0.45);
            c = mix(c, c * vec3(1.38, 1.18, 0.7), dry * 0.7);
            float band = step(0.5, fract(UV.x / 14.0));
            c *= mix(1.0, mix(0.93, 1.07, band), stripes);
            ALBEDO = c;
            ROUGHNESS = 0.96;
            SPECULAR = 0.22;
        }
        """;

    /// <summary>Advertising wall (UV = metres along, metres below the top): ad panels over a concrete footing.</summary>
    private const string WallCode = """
        shader_type spatial;
        render_mode cull_disabled;

        uniform sampler2D ads : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float panel_length = 16.0;
        uniform float ad_height = 1.0;

        void fragment() {
            float v = UV.y;
            vec3 c;
            float rough;
            if (!FRONT_FACING) {
                c = vec3(0.2, 0.22, 0.2);
                rough = 0.95;
            } else if (v < ad_height) {
                c = texture(ads, vec2(UV.x / panel_length, v / ad_height)).rgb;
                rough = 0.5;
            } else {
                c = vec3(0.28, 0.29, 0.3);
                rough = 0.92;
            }
            ALBEDO = c;
            ROUGHNESS = rough;
            SPECULAR = 0.4;
        }
        """;

    /// <summary>Catch fence: posts every 3 m, two rails, diamond mesh that fades to an average tint with distance.</summary>
    private const string FenceCode = """
        shader_type spatial;
        render_mode cull_disabled, blend_mix, depth_draw_opaque;

        uniform vec4 wire : source_color = vec4(0.66, 0.69, 0.72, 1.0);
        uniform vec4 steel : source_color = vec4(0.42, 0.43, 0.45, 1.0);

        void fragment() {
            float u = UV.x;
            float v = UV.y;
            float px = max(fwidth(u), 1e-4);
            float post = 1.0 - smoothstep(0.06, 0.06 + px * 1.5, abs(fract(u / 3.0 + 0.5) - 0.5) * 3.0);
            float top = 1.0 - smoothstep(0.035, 0.035 + px * 1.5, abs(v - 0.06));
            float mid = 1.0 - smoothstep(0.03, 0.03 + px * 1.5, abs(v - 2.2));
            float frame = max(post, max(top, mid));
            vec2 d = vec2(u + v, u - v) / 0.07;
            vec2 f = abs(fract(d) - 0.5);
            float w = max(fwidth(d.x), 1e-4);
            float lines = 1.0 - smoothstep(0.0, w * 1.2 + 0.05, 0.5 - max(f.x, f.y));
            float mesh = mix(lines * 0.9, 0.2, smoothstep(0.12, 0.45, w));
            ALBEDO = mix(wire.rgb, steel.rgb, frame);
            ALPHA = max(frame, mesh);
            METALLIC = 0.55;
            ROUGHNESS = 0.45;
        }
        """;

    /// <summary>Gradient sky with a sun glow and soft high cloud; also the ambient and reflection source.</summary>
    private const string SkyCode = """
        shader_type sky;

        uniform vec3 zenith : source_color = vec3(0.15, 0.36, 0.76);
        uniform vec3 horizon : source_color = vec3(0.74, 0.83, 0.92);
        uniform vec3 ground : source_color = vec3(0.36, 0.38, 0.30);
        uniform sampler2D clouds : hint_default_black, filter_linear_mipmap, repeat_enable;
        uniform float cloud_cover = 0.42;

        void sky() {
            vec3 dir = EYEDIR;
            float h = clamp(dir.y, 0.0, 1.0);
            vec3 c = mix(horizon, zenith, pow(h, 0.5));
            if (dir.y < 0.0) {
                c = mix(horizon, ground, clamp(-dir.y * 6.0, 0.0, 1.0));
            }
            vec3 sun_dir = LIGHT0_DIRECTION;
            float g = max(dot(dir, sun_dir), 0.0);
            vec3 sun_col = LIGHT0_COLOR;
            if (dir.y > 0.0) {
                vec2 uv = dir.xz / (dir.y + 0.12) * 0.22;
                float n = texture(clouds, uv).r * 0.65 + texture(clouds, uv * 2.7 + vec2(0.3, 0.1)).r * 0.35;
                float cloud = smoothstep(1.0 - cloud_cover, 1.0 - cloud_cover + 0.35, n) * smoothstep(0.0, 0.18, dir.y);
                vec3 cloud_col = mix(vec3(0.95, 0.96, 0.98), vec3(0.72, 0.76, 0.82), smoothstep(0.55, 1.0, n));
                cloud_col += sun_col * pow(g, 6.0) * 0.25;
                c = mix(c, cloud_col, cloud * 0.85);
            }
            c += sun_col * (pow(g, 8.0) * 0.18 + pow(g, 64.0) * 0.25);
            if (!AT_CUBEMAP_PASS) {
                c += sun_col * smoothstep(0.9996, 0.99985, g) * 8.0;
            }
            COLOR = c;
        }
        """;

    private static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);

    public static NoiseTexture2D Noise(int size, float frequency, int seed, FastNoiseLite.NoiseTypeEnum type = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, int octaves = 4)
    {
        return new NoiseTexture2D
        {
            Width = size,
            Height = size,
            Seamless = true,
            GenerateMipmaps = true,
            Normalize = true,
            Noise = new FastNoiseLite
            {
                NoiseType = type,
                Frequency = frequency,
                Seed = seed,
                FractalOctaves = octaves,
            },
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

    private static readonly Lazy<NoiseTexture2D> Grain = new(() => Noise(512, 0.06f, 3, FastNoiseLite.NoiseTypeEnum.Value, 3));
    private static readonly Lazy<NoiseTexture2D> Blotch = new(() => Noise(256, 0.012f, 9));

    private static ShaderMaterial AsphaltLike(string key, float grain, float racingLine, float roughness, float brightness = 1) =>
        Cached(key, () =>
        {
            var m = new ShaderMaterial { Shader = new Shader { Code = AsphaltCode } };
            m.SetShaderParameter("grain", Grain.Value);
            m.SetShaderParameter("blotch", Blotch.Value);
            m.SetShaderParameter("grain_strength", grain);
            m.SetShaderParameter("racing_line", racingLine);
            m.SetShaderParameter("roughness_base", roughness);
            m.SetShaderParameter("brightness", brightness);
            return m;
        });

    /// <summary>Pit lane asphalt, a little lighter and without a racing line (the track surface is in <see cref="TrackMaterials"/>).</summary>
    public static ShaderMaterial PitLane => AsphaltLike("pitlane", 1, 0, 0.88f, 1.2f);

    public static ShaderMaterial Lines => AsphaltLike("lines", 0.3f, 0, 0.6f);

    private static ShaderMaterial GrassLike(string key, float stripes) => Cached(key, () =>
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = GrassCode } };
        m.SetShaderParameter("big", Noise(256, 0.02f, 21, octaves: 5));
        m.SetShaderParameter("fine", Noise(256, 0.05f, 33, FastNoiseLite.NoiseTypeEnum.Value, 3));
        m.SetShaderParameter("stripes", stripes);
        return m;
    });

    public static ShaderMaterial Terrain => GrassLike("terrain", 0);

    /// <summary>The wall material; its ad texture arrives from <see cref="AdBoards"/> once rendered.</summary>
    public static ShaderMaterial Walls => Cached("walls", () => new ShaderMaterial { Shader = new Shader { Code = WallCode } });

    public static ShaderMaterial Fence => Cached("fence", () => new ShaderMaterial { Shader = new Shader { Code = FenceCode } });

    public static ShaderMaterial Sky => Cached("sky", () =>
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = SkyCode } };
        m.SetShaderParameter("clouds", Noise(512, 0.008f, 77, octaves: 6));
        return m;
    });

    public static StandardMaterial3D Plain(string hex, float roughness = 0.7f, float metallic = 0) =>
        Cached($"plain:{hex}:{roughness}:{metallic}", () => new StandardMaterial3D
        {
            AlbedoColor = new Color(hex),
            Roughness = roughness,
            Metallic = metallic,
        });

    /// <summary>Vertex-coloured (sRGB) material, optionally multiplied by per-instance MultiMesh colour.</summary>
    public static StandardMaterial3D VertexColour(float roughness, float metallic = 0, string key = "") =>
        Cached($"vc:{roughness}:{metallic}:{key}", () => new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = roughness,
            Metallic = metallic,
        });

    public static StandardMaterial3D Glass => Cached("glass", () => new StandardMaterial3D
    {
        AlbedoColor = new Color("1b2a40"),
        Metallic = 0.85f,
        Roughness = 0.06f,
    });

    public static StandardMaterial3D Emissive(string hex, float energy) => Cached($"emit:{hex}:{energy}", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(hex),
        EmissionEnabled = true,
        Emission = new Color(hex),
        EmissionEnergyMultiplier = energy,
        Roughness = 0.3f,
    });

    // ---- Cars ----------------------------------------------------------------------------------------------

    /// <summary>Livery paint: gloss with clearcoat, or matte (Red Bull). Vertex colours carry the livery zones.</summary>
    public static StandardMaterial3D Paint(bool matte) => Cached(matte ? "paint:matte" : "paint:gloss", () =>
    {
        var m = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Metallic = matte ? 0.1f : 0.2f,
            Roughness = matte ? 0.6f : 0.25f,
            MetallicSpecular = 0.5f,
        };
        if (!matte)
        {
            m.ClearcoatEnabled = true;
            m.Clearcoat = 1;
            m.ClearcoatRoughness = 0.06f;
        }
        return m;
    });

    public static StandardMaterial3D Carbon => VertexColour(0.38f, 0.35f, "carbon");

    public static StandardMaterial3D Tyre => VertexColour(0.86f, 0, "tyre");

    public static StandardMaterial3D Rim => VertexColour(0.25f, 0.95f, "rim");

    public static StandardMaterial3D Halo(bool matte) => VertexColour(matte ? 0.55f : 0.3f, 0.15f, "halo");

    public static StandardMaterial3D Helmet => Cached("helmet", () => new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 0.18f,
        ClearcoatEnabled = true,
        Clearcoat = 1,
    });

    public static StandardMaterial3D RainLight => Cached("rainlight", () => new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        EmissionEnabled = true,
        Emission = new Color("ff1a12"),
        EmissionEnergyMultiplier = 2.5f,
        Roughness = 0.3f,
    });
}
