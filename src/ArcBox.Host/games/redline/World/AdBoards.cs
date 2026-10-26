using ArcBox.Host.Shell;
using Godot;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// The advertising-wall artwork: four 4 m panels (16 m before it repeats) with the game's own REDLINE and ARCBOX marks in
/// neutral broadcast colours; no team, sponsor or series brands. Rendered once through an off-screen
/// <see cref="SubViewport"/>, copied into a mipmapped <see cref="ImageTexture"/> (viewport textures have no mipmaps and
/// shimmer down a long wall) and cached for the process.
/// </summary>
public static class AdBoards
{
    public const float PanelMetres = 4;
    public const int Panels = 4;
    private const int PixelsPerMetre = 160;

    private static ImageTexture? _texture;

    /// <summary>Sets the wall material's ad texture, rendering it first if needed. <paramref name="host"/> hosts the viewport for a frame.</summary>
    public static async void Apply(Node host, ShaderMaterial walls)
    {
        walls.SetShaderParameter("panel_length", PanelMetres * Panels);
        if (_texture is null)
        {
            var w = (int)(PanelMetres * Panels * PixelsPerMetre);
            var h = PixelsPerMetre;
            var vp = new SubViewport
            {
                Size = new Vector2I(w, h),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                TransparentBg = false,
                Disable3D = true,
            };
            var row = new HBoxContainer { Size = new Vector2(w, h) };
            row.AddThemeConstantOverride("separation", 0);
            (string Text, string Bg, string Fg)[] designs =
            [
                ("REDLINE", "f4f4f4", "e10600"),
                ("ARCBOX", "e10600", "ffffff"),
                ("REDLINE", "15151b", "ffffff"),
                ("ARCBOX", "dfe5ec", "15151b"),
            ];
            foreach (var (text, bg, fg) in designs)
            {
                var panel = new ColorRect { Color = new Color(bg), CustomMinimumSize = new Vector2(PanelMetres * PixelsPerMetre, h) };
                var label = new Label
                {
                    Text = text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                label.AddThemeFontOverride("font", ArcTheme.BlackItalic);
                label.AddThemeFontSizeOverride("font_size", (int)(h * 0.78f));
                label.AddThemeColorOverride("font_color", new Color(fg));
                panel.AddChild(label);
                // A thin dark seam between panels, like real board joints.
                var seam = new ColorRect { Color = new Color(0, 0, 0, 0.35f), Size = new Vector2(3, h) };
                panel.AddChild(seam);
                row.AddChild(panel);
            }
            vp.AddChild(row);
            host.AddChild(vp);

            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (_texture is null && GodotObject.IsInstanceValid(vp))
            {
                var image = vp.GetTexture().GetImage();
                image.GenerateMipmaps();
                _texture = ImageTexture.CreateFromImage(image);
            }
            if (GodotObject.IsInstanceValid(vp))
            {
                vp.QueueFree();
            }
        }
        if (_texture is not null)
        {
            walls.SetShaderParameter("ads", _texture);
        }
    }
}
