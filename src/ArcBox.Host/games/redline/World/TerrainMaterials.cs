using Godot;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;
using NVector2 = System.Numerics.Vector2;
using Plan = Redline.Presentation.Course.Surroundings;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// The ground's materials (spatial shaders, procedural noise textures, no image assets): dry Texas grass with colour
/// variation, dirt patches and mown bands near the track (the terrain and the far land), the car parks' and compound
/// pads' surfaces, the service roads and the Grand Plaza's paving. The terrain mesh carries its own attributes in UV2
/// (<see cref="SurroundMeshes.TerrainAttributes"/>: distance to the centre line, wear). Materials are cached and shared.
/// </summary>
public static class TerrainMaterials
{
    private const string SrgbToLinear = """
        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }
        """;

    /// <summary>
    /// Terrain and far land (<see cref="TerrainAttribute"/>: UV2 = (metres from the track's centre line, wear), COLOR.r =
    /// infield, COLOR.g = hollow). Near the track: mown grass in bands that follow the track (light and dark by mowing
    /// direction), with a ragged edge; beyond, golden and olive grass with broad green zones on low ground, in the
    /// infield and in large pasture patches, beige dry ground in a minority of places; dust where the wear is.
    /// </summary>
    private const string GroundCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D big : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D blades : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float mown_reach = 42.0;
        varying vec3 world_pos;
        varying vec2 attr;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            attr = UV2;
        }

        void fragment() {
            vec2 p = world_pos.xz;
            float infield = COLOR.r;
            float hollow = COLOR.g;
            float n1 = texture(big, p * 0.0009).r;
            float n0 = texture(big, p * 0.0041 + vec2(0.31, 0.77)).r;
            float nm = texture(big, p * 0.017 + vec2(0.6, 0.2)).r;
            float n2 = texture(fine, p * 0.071).r;
            float n3 = texture(fine, p * 0.53 + vec2(0.21, 0.4)).r;

            // Autumn in central Texas: golden and olive grass, broad green zones (the draws and low ground, the watered
            // infield, big patches of pasture), beige dry ground a minority.
            vec3 gold = srgb_to_linear(vec3(0.64, 0.56, 0.32));
            vec3 olive = srgb_to_linear(vec3(0.50, 0.49, 0.27));
            vec3 green = srgb_to_linear(vec3(0.37, 0.46, 0.21));
            vec3 deep = srgb_to_linear(vec3(0.28, 0.37, 0.17));
            vec3 beige = srgb_to_linear(vec3(0.69, 0.62, 0.46));
            vec3 dry = mix(olive, gold, smoothstep(0.3, 0.72, n0));
            float greenness = clamp(0.22 + 0.8 * smoothstep(0.36, 0.7, n1) + 0.9 * hollow + 0.35 * infield + 0.3 * (nm - 0.5), 0.0, 1.0);
            vec3 c = mix(dry, mix(green, deep, hollow), greenness * 0.85);
            float bare = smoothstep(0.7, 0.8, texture(big, p * 0.0053 + vec2(0.13, 0.52)).r * 0.85 + nm * 0.15) * (1.0 - greenness);
            c = mix(c, beige, bare * 0.65);
            c *= mix(0.9, 1.08, nm);

            // Mown grass near the track: greener, in bands along the track, with a ragged edge.
            float d = attr.x;
            float edge = mown_reach + 18.0 * (n0 - 0.5) + 6.0 * (nm - 0.5);
            float mown = 1.0 - smoothstep(edge - 4.0, edge + 4.0, d);
            vec3 cut = mix(srgb_to_linear(vec3(0.40, 0.52, 0.22)), srgb_to_linear(vec3(0.47, 0.55, 0.26)), n0);
            float band = smoothstep(0.42, 0.58, fract(d / 7.0)) * (1.0 - smoothstep(0.92, 1.0, fract(d / 7.0)));
            cut *= mix(0.92, 1.07, band) * mix(0.94, 1.04, nm);
            c = mix(c, cut, mown);

            // Wear round the car parks and roads: dust and trampled grass.
            vec3 soil = mix(srgb_to_linear(vec3(0.52, 0.41, 0.30)), srgb_to_linear(vec3(0.64, 0.57, 0.46)), nm);
            float wear = smoothstep(0.05, 1.0, attr.y) * mix(0.55, 1.0, n3);
            c = mix(c, mix(soil, dry, 0.35), wear * 0.75);

            // Fine variation close up only (far away it reads as speckle).
            float far_ = smoothstep(80.0, 500.0, length(VERTEX));
            c *= mix(mix(0.9, 1.07, n2) * mix(0.93, 1.05, n3), 1.0, far_);
            ALBEDO = c;
            ROUGHNESS = 0.95;
            SPECULAR = 0.2;
            float fade = 1.0 - smoothstep(30.0, 120.0, length(VERTEX));
            NORMAL_MAP = texture(blades, p * 0.45).rgb;
            NORMAL_MAP_DEPTH = 0.7 * fade;
        }
        """;

    /// <summary>
    /// Car parks and compound pads (UV = metres along, metres across from the start edge; COLOR.r = the surface:
    /// 0 grass lot with dirt aisles and trampled rows, 0.5 gravel pad, 1 asphalt with white bays).
    /// </summary>
    private static readonly string LotCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D big : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform float margin = {{F(Plan.LotMargin)}};
        uniform float row = {{F(Plan.RowDepth)}};
        uniform float aisle = {{F(Plan.Aisle)}};
        uniform float bay = {{F(Plan.BayPitch)}};
        varying vec3 world_pos;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        float line(float x, float w) {
            float px = max(fwidth(x), 1e-4);
            return 1.0 - smoothstep(w, w + px * 1.5, abs(x));
        }

        void fragment() {
            vec2 p = world_pos.xz;
            float n2 = texture(fine, p * 0.09).r;
            float n3 = texture(fine, p * 0.7 + vec2(0.3, 0.1)).r;
            float nb = texture(big, p * 0.02).r;
            float period = 2.0 * row + aisle;
            float v = UV.y - margin;
            float t = mod(v, period);
            bool inside = v > 0.0;
            float in_aisle = inside ? smoothstep(2.0 * row - 0.4, 2.0 * row + 0.6, t) * (1.0 - smoothstep(period - 0.6, period, t)) : 1.0;
            float kind = COLOR.r;
            vec3 c;
            float rough = 0.95;
            if (kind > 0.75) {
                c = srgb_to_linear(vec3(0.27, 0.275, 0.29)) * mix(0.85, 1.12, n2) * mix(0.9, 1.08, nb);
                float u = UV.x - margin;
                float bays = inside && t < 2.0 * row && u > 0.0 ? line(fract(u / bay + 0.5) - 0.5, 0.045) : 0.0;
                float ends = inside ? max(line(t, 0.07), max(line(t - row, 0.07), line(t - 2.0 * row, 0.07))) * step(t, 2.0 * row + 0.2) : 0.0;
                c = mix(c, vec3(0.8), max(bays, ends) * 0.9);
                rough = 0.85;
            } else if (kind > 0.25) {
                c = srgb_to_linear(vec3(0.71, 0.66, 0.55)) * mix(0.8, 1.12, n3) * mix(0.9, 1.05, n2);
            } else {
                vec3 grass = mix(srgb_to_linear(vec3(0.55, 0.53, 0.33)), srgb_to_linear(vec3(0.62, 0.58, 0.38)), n2);
                vec3 dirt = srgb_to_linear(vec3(0.56, 0.45, 0.33));
                // Two tyre tracks along each aisle.
                float rut = inside ? max(line(t - (2.0 * row + aisle * 0.3), 0.5), line(t - (2.0 * row + aisle * 0.7), 0.5)) : 0.0;
                c = mix(grass, dirt, clamp(in_aisle * 0.55 + rut * 0.45 * in_aisle, 0.0, 1.0) * mix(0.7, 1.0, n3));
                c *= mix(0.88, 1.08, n3);
            }
            ALBEDO = c;
            ROUGHNESS = rough;
            SPECULAR = 0.3;
        }
        """;

    /// <summary>Service roads: asphalt with world-space grain (overlaps at junctions match), dusty edges.</summary>
    private static readonly string RoadCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D big : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform float half_width = {{F(Plan.RoadWidth / 2)}};
        varying vec3 world_pos;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec2 p = world_pos.xz;
            float n2 = texture(fine, p * 0.31).r;
            float n3 = texture(fine, p * 1.7 + vec2(0.4, 0.1)).r;
            float nb = texture(big, p * 0.013).r;
            vec3 c = srgb_to_linear(COLOR.rgb) * mix(0.82, 1.15, n2) * mix(0.92, 1.06, n3) * mix(0.9, 1.1, nb);
            float edge = smoothstep(half_width - 0.9, half_width, abs(UV.y));
            c = mix(c, srgb_to_linear(vec3(0.58, 0.52, 0.42)), edge * 0.7 * n2);
            ALBEDO = c;
            ROUGHNESS = 0.88;
            SPECULAR = 0.4;
        }
        """;

    /// <summary>
    /// Plaza paving: warm concrete pavers (a 1.2 m grid in world space) with red granite bands; COLOR.r = 1 on the
    /// promenade (UV.x = metres across from its middle line), 0 on the tower's apron (UV.x = radius).
    /// </summary>
    private const string PlazaCode = $$"""
        shader_type spatial;
        render_mode cull_back;

        uniform sampler2D fine : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        varying vec3 world_pos;

        {{SrgbToLinear}}

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        float band(float x, float a, float b) {
            float px = max(fwidth(x), 1e-4);
            return smoothstep(a - px, a + px, x) * (1.0 - smoothstep(b - px, b + px, x));
        }

        void fragment() {
            vec2 p = world_pos.xz;
            float n2 = texture(fine, p * 0.23).r;
            float n3 = texture(fine, p * 1.3 + vec2(0.2, 0.6)).r;
            vec3 c = srgb_to_linear(vec3(0.80, 0.77, 0.71)) * mix(0.9, 1.06, n2) * mix(0.94, 1.04, n3);
            vec2 g = abs(fract(p / 1.2) - 0.5);
            float px = max(fwidth(p.x / 1.2), 1e-4);
            float joint = 1.0 - smoothstep(0.46, 0.46 + px * 2.0, max(g.x, g.y));
            c *= mix(1.0, 0.86, 1.0 - joint);
            float x = abs(UV.x);
            float red = COLOR.r > 0.5
                ? max(band(x, 5.0, 6.2), band(x, 12.6, 13.4))
                : max(band(x, 29.0, 30.5), max(band(x, 35.4, 36.4), band(x, 6.0, 7.0)));
            c = mix(c, srgb_to_linear(vec3(0.55, 0.21, 0.18)) * mix(0.9, 1.08, n3), red);
            ALBEDO = c;
            ROUGHNESS = 0.8;
            SPECULAR = 0.35;
        }
        """;

    /// <summary>A float literal for shader code (invariant culture, always with a decimal point).</summary>
    private static string F(float v) => v.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);

    private static readonly Lazy<NoiseTexture2D> Big = new(() => Materials.Noise(512, 0.012f, 61, octaves: 5));
    private static readonly Lazy<NoiseTexture2D> Fine = new(() => Materials.Noise(256, 0.05f, 67, FastNoiseLite.NoiseTypeEnum.Value, 3));

    private static readonly Lazy<NoiseTexture2D> Blades = new(() =>
    {
        var tex = Materials.Noise(256, 0.09f, 71, FastNoiseLite.NoiseTypeEnum.Cellular, 2);
        tex.AsNormalMap = true;
        tex.BumpStrength = 6;
        return tex;
    });

    private static ShaderMaterial Shader(string key, string code, bool blades = false) => (ShaderMaterial)Cached(key, () =>
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = code } };
        m.SetShaderParameter("big", Big.Value);
        m.SetShaderParameter("fine", Fine.Value);
        if (blades)
        {
            m.SetShaderParameter("blades", Blades.Value);
        }
        return m;
    });

    public static ShaderMaterial Ground => Shader("ground", GroundCode, blades: true);

    public static ShaderMaterial Lots => Shader("lots", LotCode);

    public static ShaderMaterial Roads => Shader("roads", RoadCode);

    public static ShaderMaterial Plaza => Shader("plaza", PlazaCode);

    private static Material Cached(string key, Func<Material> make)
    {
        if (!Cache.TryGetValue(key, out var m))
        {
            Cache[key] = m = make();
        }
        return m;
    }

    /// <summary>The terrain mesh with its attributes: UV2 = (distance, wear), COLOR = (infield, hollow, 0, 1).</summary>
    public static ArrayMesh Terrain(MeshData data, IReadOnlyList<TerrainAttribute> attrs)
    {
        var arrays = MeshConvert.Arrays(data);
        var u2 = new Vector2[attrs.Count];
        var colours = new Color[attrs.Count];
        for (var i = 0; i < u2.Length; i++)
        {
            u2[i] = new Vector2(attrs[i].Distance, attrs[i].Wear);
            colours[i] = new Color(attrs[i].Infield ? 1 : 0, attrs[i].Hollow, 0);
        }
        arrays[(int)Mesh.ArrayType.TexUV2] = u2;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
