using ArcBox.Host.Shell;
using Godot;

namespace ArcBox.Host.Games.Redline.Podium;

/// <summary>
/// The podium's backdrop artwork, a REDLINE step-and-repeat media wall: dark panels carrying staggered REDLINE and
/// ARCBOX marks, the red REDLINE slab in the middle above the winner, and a
/// chequered band along the foot. Drawn once through an off-screen <see cref="SubViewport"/>, copied into a mipmapped
/// texture and cached for the process (like the ad boards).
/// </summary>
public static partial class PodiumBackdrop
{
    public const int Width = 2048;
    public const int Height = 922;

    private static ImageTexture? _texture;

    /// <summary>Sets <paramref name="material"/>'s albedo to the artwork, drawing it first if needed.</summary>
    public static async void Apply(Node host, StandardMaterial3D material)
    {
        if (_texture is null)
        {
            var vp = new SubViewport
            {
                Size = new Vector2I(Width, Height),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
                TransparentBg = false,
                Disable3D = true,
            };
            var art = new Art { Size = new Vector2(Width, Height) };
            vp.AddChild(art);
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
        if (_texture is not null && GodotObject.IsInstanceValid(material))
        {
            material.AlbedoTexture = _texture;
        }
    }

    private partial class Art : Control
    {
        public override void _Draw()
        {
            var w = Size.X;
            var h = Size.Y;
            DrawRect(new Rect2(0, 0, w, h), new Color("121219"));
            // Panel joints and a soft vertical sheen, like an LED wall made of tiles.
            for (var x = 0f; x < w; x += 256)
            {
                DrawRect(new Rect2(x, 0, 2, h), new Color(1, 1, 1, 0.05f));
            }
            for (var i = 0; i < 24; i++)
            {
                var a = 0.035f * (1 - (Mathf.Abs(i - 12) / 12f));
                DrawRect(new Rect2(0, i * (h / 24f), w, h / 24f), new Color(1, 1, 1, a));
            }

            // Step-and-repeat: staggered rows of the two marks.
            var font = ArcTheme.BlackItalic;
            const int size = 46;
            const float colW = 300, rowH = 96;
            for (var row = 0; row < 10; row++)
            {
                for (var col = -1; col < 8; col++)
                {
                    var redline = (row + col) % 2 == 0;
                    var x = (col * colW) + (row % 2 == 0 ? 0 : colW / 2) + 30;
                    var y = 60 + (row * rowH);
                    DrawString(font, new Vector2(x, y), redline ? "REDLINE" : "ARCBOX", HorizontalAlignment.Left, -1, size,
                        redline ? new Color(0.88f, 0.1f, 0.08f, 0.55f) : new Color(1, 1, 1, 0.16f));
                }
            }

            // The centre: a dark plate behind the red slab.
            var plate = new Rect2((w / 2) - 560, 70, 1120, 330);
            DrawRect(plate, new Color("0b0b10"));
            DrawRect(new Rect2(plate.Position.X, plate.End.Y - 6, plate.Size.X, 6), ArcTheme.Red);
            const float slabW = 900, slabH = 250, slant = 46;
            var sx = (w / 2) - (slabW / 2);
            DrawColoredPolygon([new(sx + slant, 110), new(sx + slabW, 110), new(sx + slabW - slant, 110 + slabH), new(sx, 110 + slabH)], ArcTheme.Red);
            const int slabFont = 186;
            var tw = font.GetStringSize("REDLINE", HorizontalAlignment.Left, -1, slabFont).X;
            DrawString(font, new Vector2((w / 2) - (tw / 2), 110 + (slabH / 2) + (font.GetAscent(slabFont) * 0.36f)), "REDLINE",
                HorizontalAlignment.Left, -1, slabFont, Colors.White);

            // The chequered foot.
            const float cell = 22;
            DrawRect(new Rect2(0, h - (2 * cell) - 10, w, 10), ArcTheme.Red);
            for (var c = 0; c < w / cell; c++)
            {
                for (var r = 0; r < 2; r++)
                {
                    DrawRect(new Rect2(c * cell, h - (2 * cell) + (r * cell), cell, cell), (c + r) % 2 == 0 ? new Color("f2f2f2") : new Color("15151b"));
                }
            }
        }
    }
}
