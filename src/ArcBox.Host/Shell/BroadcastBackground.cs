using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// Full-rect broadcast backdrop: 160° gradient #1c1c26→#0b0b10, a soft glow top-right and faint 115° pinstripes
/// (the <c>.tv</c> background in broadcast.css).
/// </summary>
public partial class BroadcastBackground : ColorRect
{
    private const string ShaderCode = """
        shader_type canvas_item;

        uniform vec3 top_colour = vec3(0.110, 0.110, 0.149);
        uniform vec3 bottom_colour = vec3(0.043, 0.043, 0.063);
        uniform vec3 glow_colour = vec3(0.165, 0.165, 0.227);
        uniform vec2 glow_centre = vec2(0.85, 0.2);

        void fragment() {
            vec2 uv = UV;
            // CSS 160deg: towards the bottom, slightly left.
            vec2 dir = normalize(vec2(-0.342, 0.940));
            float t = clamp(dot(uv - 0.5, dir) / 0.9 + 0.5, 0.0, 1.0);
            vec3 c = mix(top_colour, bottom_colour, t);
            vec2 d = (uv - glow_centre) / vec2(0.7, 0.8);
            c = mix(c, glow_colour, (1.0 - smoothstep(0.0, 0.7, length(d))) * 0.85);
            // Pinstripes: 2px of every 22px along 115deg, in screen pixels.
            float a = radians(115.0);
            float s = mod(dot(FRAGCOORD.xy, vec2(cos(a), sin(a))), 22.0);
            c += (1.0 - step(2.0, s)) * 0.018;
            // Gentle vignette.
            float v = smoothstep(1.25, 0.35, length((uv - 0.5) * vec2(1.4, 1.2)));
            c *= mix(0.75, 1.0, v);
            COLOR = vec4(c, 1.0);
        }
        """;

    private static Shader? _shader;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Color = ArcTheme.BgBottom;
        _shader ??= new Shader { Code = ShaderCode };
        Material = new ShaderMaterial { Shader = _shader };
    }
}
