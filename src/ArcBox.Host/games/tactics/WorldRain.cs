using Godot;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>The existing rain strips tiled over the world, in screen pixels, below the HUD and loading fade.</summary>
internal sealed partial class WorldRain(string packRoot) : Node2D
{
    private readonly AmbientManifest? _manifest = AmbientManifest.Load(packRoot);
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private WeatherSheet? _sheet;
    private Texture2D? _texture;

    public void Step(string? weather, double clock)
    {
        var sheet = WeatherPlan.Of(weather, _manifest, clock);
        if (sheet == _sheet) return;
        _sheet = sheet;
        _texture = null;
        if (sheet is not null)
        {
            if (!_textures.TryGetValue(sheet.Path, out _texture))
            {
                var path = System.IO.Path.Combine(packRoot, "ambient", sheet.Path);
                if (System.IO.File.Exists(path))
                {
                    using var image = Image.LoadFromFile(path);
                    if (image is not null) _texture = ImageTexture.CreateFromImage(image);
                }
                _textures[sheet.Path] = _texture;
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sheet is not { } sheet || _texture is null) return;
        var size = GetViewportRect().Size;
        var source = new Rect2(sheet.Frame * sheet.Width, 0, sheet.Width, sheet.Height);
        for (var y = 0; y < size.Y; y += sheet.Height)
            for (var x = 0; x < size.X; x += sheet.Width)
                DrawTextureRectRegion(_texture, new Rect2(x, y, sheet.Width, sheet.Height), source);
    }

    public override void _ExitTree()
    {
        foreach (var texture in _textures.Values.OfType<Texture2D>()) texture.Dispose();
        _textures.Clear();
    }
}
