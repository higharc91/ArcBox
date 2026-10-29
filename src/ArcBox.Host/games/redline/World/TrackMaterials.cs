using Godot;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// The circuit surface materials (spatial shaders, procedural textures only): asphalt with aggregate, the rubbered racing
/// line, skid marks and marbles; painted markings and sealant; kerbs (flat, sawtooth ridges, sausage); painted and plain
/// run-off; gravel; mown verge. The track meshes' data channels are documented in <c>TrackMeshBuilder</c> and
/// <c>CourseMeshes</c>: asphalt CUSTOM0 = (racing-line lateral, rubber, skid, marbles), kerb CUSTOM0.x = kind. Materials
/// are cached statically and shared between scene instances; <see cref="SetDetail"/> turns the skid, marble and
/// run-off tyre-mark detail off for the Low preset.
/// </summary>
public static class TrackMaterials
{
    private const string Common = """
        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }

        // Bump mapping without tangents (Mikkelsen 2010): perturbs the view-space normal by the screen-space gradient of h.
        vec3 bump(vec3 n, vec3 p, float h, float strength) {
            vec3 dpdx = dFdx(p);
            vec3 dpdy = dFdy(p);
            vec3 r1 = cross(dpdy, n);
            vec3 r2 = cross(n, dpdx);
            float det = dot(dpdx, r1);
            vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
            return normalize(abs(det) * n - strength * grad);
        }
        """;

    /// <summary>Asphalt. UV = (metres along the lap, metres left of centre); COLOR = surface tone; CUSTOM0 = wear data.</summary>
    private const string AsphaltCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D grain : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D blotch : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D pellets : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float detail = 1.0;
        varying vec4 wear;
        varying vec3 world_pos;

        {{Common}}

        void vertex() {
            wear = CUSTOM0;
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float s = UV.x;
            float lat = UV.y;
            float dist = length(VERTEX);
            vec3 base = srgb_to_linear(COLOR.rgb);

            // Aggregate: three scales of grain, light stones in the mix, broad blotches of wear and oil.
            float g1 = texture(grain, UV * 0.29).r;
            float g2 = texture(grain, UV * 1.41 + vec2(0.37, 0.11)).r;
            float g3 = texture(grain, UV * 6.3 + vec2(0.71, 0.29)).r;
            float stones = smoothstep(0.7, 0.88, g3) * (1.0 - smoothstep(30.0, 90.0, dist));
            float b = texture(blotch, world_pos.xz * 0.011).r;
            float b2 = texture(blotch, world_pos.xz * 0.047 + vec2(0.5)).r;
            // Patchy darker and lighter areas tens of metres across, so the surface reads at broadcast distance.
            float b3 = texture(blotch, world_pos.xz * 0.0045 + vec2(0.21, 0.63)).r;
            float b4 = texture(blotch, vec2(s * 0.006, lat * 0.03) + vec2(0.7, 0.2)).r;
            float near = 1.0 - smoothstep(30.0, 90.0, dist);
            vec3 c = base * mix(0.76, 1.2, g1) * mix(0.86, 1.1, g2) * mix(0.86, 1.08, b) * mix(0.93, 1.05, b2)
                * mix(0.82, 1.14, smoothstep(0.2, 0.8, b3)) * mix(0.9, 1.08, b4);
            // Close up: dark pores between the stones and light aggregate chips.
            float pore = smoothstep(0.3, 0.12, g3);
            c *= 1.0 - 0.35 * pore * near;
            c = mix(c, c * 1.8, stones * 0.45);

            // The rubbered line: a soft dark band with the two tyre tracks darker still, streaked along the travel.
            float d = lat - wear.x;
            float band = exp(-pow(d / 2.1, 2.0));
            float tracks = exp(-pow((abs(d) - 0.85) / 0.45, 2.0));
            float streak = texture(grain, vec2(lat * 2.3, s * 0.031)).r;
            float rubber = wear.y * (0.6 * band + 0.55 * tracks) * mix(0.75, 1.2, streak);
            // Off line the surface is dustier and lighter; on line it is rubbered dark.
            c *= mix(1.16, 1.0, band);
            c *= 1.0 - 0.68 * clamp(rubber, 0.0, 1.0);

            // Skid marks: long dark streaks around the line in the braking zones, densest under the tyres.
            float sk = wear.z * detail;
            if (sk > 0.002) {
                float lane = exp(-pow(d / 2.8, 2.0));
                float pair = max(tracks, exp(-pow((abs(d) - 0.8) / 0.6, 2.0)) * 0.6);
                float st = texture(grain, vec2(lat * 3.3 + 0.5, s * 0.011)).r;
                float st2 = texture(grain, vec2(lat * 8.1 + 0.2, s * 0.019 + 0.3)).r;
                float st3 = texture(grain, vec2(lat * 5.7 + 0.8, s * 0.015 + 0.6)).r;
                // More streaks, and darker, where the skid weight is highest: right at the braking point.
                float density = mix(0.62, 0.4, sk);
                float marks = smoothstep(density, density + 0.18, st) * 0.85 + smoothstep(density + 0.08, density + 0.24, st2) * 0.65
                    + smoothstep(density + 0.04, density + 0.2, st3) * 0.5 * sk;
                c *= 1.0 - clamp(sk * (0.45 * lane + 0.85 * pair) * marks, 0.0, 1.0) * 0.92;
            }

            // Marbles: rubber pellets just off line on the outside of the corner exit.
            float mb = abs(wear.w) * detail;
            if (mb > 0.002) {
                float out_d = d * sign(wear.w);
                float region = smoothstep(1.2, 2.2, out_d) * (1.0 - smoothstep(3.8, 6.5, out_d));
                float dots = smoothstep(0.55, 0.75, texture(pellets, UV * 1.7).r);
                float dots2 = smoothstep(0.6, 0.8, texture(pellets, UV * 0.8 + vec2(0.4, 0.1)).r);
                float dust = texture(grain, UV * 0.7 + vec2(0.13, 0.5)).r;
                c *= 1.0 - region * mb * (0.75 * max(dots, dots2 * 0.8) + 0.22 * dust);
            }

            ALBEDO = c;
            ROUGHNESS = clamp(0.9 - 0.1 * g2 - 0.25 * rubber - 0.08 * stones + 0.05 * pore, 0.35, 1.0);
            SPECULAR = 0.45;
            float h = g3 * 0.6 + g2 * 0.4;
            NORMAL = bump(NORMAL, VERTEX, h, 0.008 * (1.0 - smoothstep(25.0, 70.0, dist)));
        }
        """;

    /// <summary>Paint and sealant (vertex colour): white line paint is satin, black sealant glossy.</summary>
    private const string PaintCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D grain : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

        {{Common}}

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            float lum = dot(COLOR.rgb, vec3(0.333));
            float g = texture(grain, UV * 0.93 + vec2(0.4, 0.2)).r;
            float wear = texture(grain, UV * 0.17).r;
            ALBEDO = base * mix(0.9, 1.03, g) * mix(0.88, 1.0, smoothstep(0.2, 0.6, wear));
            ROUGHNESS = mix(0.28, 0.6, lum);
            SPECULAR = 0.5;
        }
        """;

    /// <summary>Kerbs. UV = (metres along the kerb, metres from its inner edge); CUSTOM0.x = 0 flat, 1 sawtooth, 2 sausage.</summary>
    private const string KerbCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D grain : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        varying flat float kind;

        {{Common}}

        void vertex() {
            kind = CUSTOM0.x;
        }

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            float g = texture(grain, UV * vec2(1.3, 1.9)).r;
            float dist = length(VERTEX);
            // Rubber smudged on from the track side, heavier on the sawtooth tops.
            float smudge = texture(grain, vec2(UV.x * 0.21, UV.y * 0.9 + 0.3)).r;
            float dirt = smoothstep(0.45, 0.85, smudge) * exp(-UV.y * 1.6) * 0.45;
            float h = 0.0;
            if (kind > 0.5 && kind < 1.5) {
                // Transverse sawtooth ridges every 0.3 m: a slow rise and a sharp drop.
                float r = fract(UV.x / 0.3);
                h = r * (1.0 - smoothstep(0.82, 1.0, r)) * smoothstep(0.1, 0.25, UV.y);
                base *= mix(0.78, 1.0, smoothstep(0.0, 0.25, r));
            }
            ALBEDO = base * mix(0.92, 1.04, g) * (1.0 - dirt);
            ROUGHNESS = kind > 1.5 ? 0.35 : mix(0.42, 0.7, dirt + 0.2 * g);
            SPECULAR = 0.5;
            NORMAL = bump(NORMAL, VERTEX, h, 0.06 * (1.0 - smoothstep(20.0, 60.0, dist)));
        }
        """;

    /// <summary>Run-off: painted stripes or plain tarmac (vertex colour); UV.y = metres from the asphalt edge.</summary>
    private const string RunoffCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D grain : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D blotch : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform float detail = 1.0;
        varying vec3 world_pos;

        {{Common}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            float g1 = texture(grain, UV * 0.33).r;
            float g2 = texture(grain, UV * 1.7 + vec2(0.2, 0.6)).r;
            float b = texture(blotch, world_pos.xz * 0.02).r;
            float b2 = texture(blotch, world_pos.xz * 0.11 + vec2(0.3)).r;
            // Worn paint: the asphalt grey shows through in patches, more where the cars run.
            float worn = smoothstep(0.55, 0.85, g1 * 0.6 + b2 * 0.6) * (0.35 + 0.65 * exp(-UV.y / 6.0));
            vec3 c = base * mix(0.86, 1.06, g1) * mix(0.92, 1.05, g2) * mix(0.88, 1.04, b);
            c = mix(c, vec3(0.052, 0.052, 0.054) * mix(0.85, 1.15, g2), worn * 0.45);
            // Tyre marks from cars running wide: long dark streaks, densest near the track and thinning outwards.
            float tm = texture(grain, vec2(UV.y * 2.6, UV.x * 0.02)).r;
            float tm2 = texture(grain, vec2(UV.y * 1.3 + 0.4, UV.x * 0.013 + 0.2)).r;
            float marks = (smoothstep(0.5, 0.72, tm) * exp(-UV.y / 5.5) + smoothstep(0.62, 0.8, tm2) * 0.6 * exp(-UV.y / 10.0)) * detail;
            c *= 1.0 - 0.55 * clamp(marks, 0.0, 1.0);
            ALBEDO = c;
            ROUGHNESS = clamp(0.86 - 0.05 * g2 + 0.06 * marks, 0.6, 1.0);
            SPECULAR = 0.3;
        }
        """;

    /// <summary>Gravel trap: pebbles from a cellular noise (per-pebble tint and rounded bumps) over raked variation.</summary>
    private const string GravelCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D cells : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D edges : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D blotch : hint_default_white, filter_linear_mipmap, repeat_enable;
        varying vec3 world_pos;

        {{Common}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec2 p = world_pos.xz;
            float dist = length(VERTEX);
            vec3 base = srgb_to_linear(COLOR.rgb);
            float tint = texture(cells, p * 0.35).r;
            float tint2 = texture(cells, p * 1.1 + vec2(0.3, 0.7)).r;
            float edge = texture(edges, p * 0.35).r;
            float edge2 = texture(edges, p * 1.1 + vec2(0.3, 0.7)).r;
            float b = texture(blotch, p * 0.015).r;
            float rake = 0.5 + 0.5 * sin(dot(p, vec2(0.83, 0.56)) * 9.0 + b * 6.0);
            vec3 c = base * mix(0.72, 1.18, tint) * mix(0.85, 1.1, tint2) * mix(0.86, 1.06, b) * mix(0.95, 1.03, rake);
            // Crevices between the stones are darker.
            c *= mix(0.62, 1.0, smoothstep(0.08, 0.35, edge)) * mix(0.8, 1.0, smoothstep(0.08, 0.35, edge2));
            ALBEDO = c;
            ROUGHNESS = 0.92;
            SPECULAR = 0.35;
            float h = smoothstep(0.0, 0.5, edge) * 0.7 + smoothstep(0.0, 0.5, edge2) * 0.3;
            NORMAL = bump(NORMAL, VERTEX, h, 0.03 * (1.0 - smoothstep(20.0, 80.0, dist)));
        }
        """;

    /// <summary>Mown verge: noise-broken vertex colour, dry patches and alternating mown lanes along the track.</summary>
    private const string VergeCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D big : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float lane_width = 3.2;
        uniform float block_length = 36.0;
        varying vec3 world_pos;

        {{Common}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec3 base = srgb_to_linear(COLOR.rgb);
            float n1 = texture(big, world_pos.xz * 0.0021).r;
            float n0 = texture(big, world_pos.xz * 0.0105 + vec2(0.3, 0.7)).r;
            float n2 = texture(fine, world_pos.xz * 0.11).r;
            float n3 = texture(fine, world_pos.xz * 0.83 + vec2(0.21, 0.4)).r;
            vec3 c = base * mix(0.74, 1.16, n1) * mix(0.88, 1.08, n0) * mix(0.88, 1.1, n2) * mix(0.9, 1.07, n3);
            float dry = smoothstep(0.55, 0.8, n1 * 0.7 + n0 * 0.45);
            c = mix(c, c * vec3(1.32, 1.15, 0.72), dry * 0.6);
            // Mown lanes parallel to the track, the mowing direction flipping every block: a broadcast checkerboard.
            float lane = floor(abs(UV.y) / lane_width);
            float block = floor(UV.x / block_length);
            float flip = mod(lane + block, 2.0);
            float edge_soft = smoothstep(0.0, 0.25, fract(abs(UV.y) / lane_width)) * smoothstep(1.0, 0.75, fract(abs(UV.y) / lane_width));
            c *= mix(1.0, mix(0.9, 1.09, flip), 0.65 + 0.35 * edge_soft);
            ALBEDO = c;
            ROUGHNESS = 0.95;
            SPECULAR = 0.22;
        }
        """;

    private static readonly Dictionary<string, ShaderMaterial> Cache = new(StringComparer.Ordinal);

    private static readonly Lazy<NoiseTexture2D> Grain = new(() => Materials.Noise(512, 0.06f, 3, FastNoiseLite.NoiseTypeEnum.Value, 3));
    private static readonly Lazy<NoiseTexture2D> Blotch = new(() => Materials.Noise(256, 0.012f, 9));
    private static readonly Lazy<NoiseTexture2D> Pellets = new(() => Cellular(256, 0.09f, 41, FastNoiseLite.CellularReturnTypeEnum.Distance, invert: true));
    private static readonly Lazy<NoiseTexture2D> CellTint = new(() => Cellular(256, 0.05f, 57, FastNoiseLite.CellularReturnTypeEnum.CellValue));
    private static readonly Lazy<NoiseTexture2D> CellEdge = new(() => Cellular(256, 0.05f, 57, FastNoiseLite.CellularReturnTypeEnum.Distance2Sub));

    private static NoiseTexture2D Cellular(int size, float frequency, int seed, FastNoiseLite.CellularReturnTypeEnum type, bool invert = false) => new()
    {
        Width = size,
        Height = size,
        Seamless = true,
        GenerateMipmaps = true,
        Normalize = true,
        Invert = invert,
        Noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular,
            Frequency = frequency,
            Seed = seed,
            CellularReturnType = type,
            CellularJitter = 0.9f,
            FractalType = FastNoiseLite.FractalTypeEnum.None,
        },
    };

    private static ShaderMaterial Make(string key, string code, Action<ShaderMaterial> setup)
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            m = new ShaderMaterial { Shader = new Shader { Code = code } };
            setup(m);
            Cache[key] = m;
        }
        return m;
    }

    public static ShaderMaterial Asphalt => Make("asphalt", AsphaltCode, m =>
    {
        m.SetShaderParameter("grain", Grain.Value);
        m.SetShaderParameter("blotch", Blotch.Value);
        m.SetShaderParameter("pellets", Pellets.Value);
    });

    public static ShaderMaterial Paint => Make("paint", PaintCode, m => m.SetShaderParameter("grain", Grain.Value));

    public static ShaderMaterial Kerbs => Make("kerbs", KerbCode, m => m.SetShaderParameter("grain", Grain.Value));

    public static ShaderMaterial Runoff => Make("runoff", RunoffCode, m =>
    {
        m.SetShaderParameter("grain", Grain.Value);
        m.SetShaderParameter("blotch", Blotch.Value);
    });

    public static ShaderMaterial Gravel => Make("gravel", GravelCode, m =>
    {
        m.SetShaderParameter("cells", CellTint.Value);
        m.SetShaderParameter("edges", CellEdge.Value);
        m.SetShaderParameter("blotch", Blotch.Value);
    });

    public static ShaderMaterial Verge => Make("verge", VergeCode, m =>
    {
        m.SetShaderParameter("big", Materials.Noise(256, 0.02f, 21, octaves: 5));
        m.SetShaderParameter("fine", Materials.Noise(256, 0.05f, 33, FastNoiseLite.NoiseTypeEnum.Value, 3));
    });

    /// <summary>Skid marks, marbles and run-off tyre marks on (Ultra, High) or off (Low).</summary>
    public static void SetDetail(bool on)
    {
        Asphalt.SetShaderParameter("detail", on ? 1f : 0f);
        Runoff.SetShaderParameter("detail", on ? 1f : 0f);
    }
}
