using Godot;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws pure ambient life on the world: butterflies over grass, fish jumping in deep water, smoke from chimneys.
/// TV-only: no gameplay logic, no view data, no network inputs.
/// </summary>
internal sealed partial class WorldAmbient : Node2D
{
    private readonly AmbientPlan? _plan;
    private readonly string _ambientRoot;
    private readonly Dictionary<string, Texture2D> _textures = [];
    private AmbientItem[] _items = [];

    public WorldAmbient(AmbientPlan? plan, string ambientRoot, int zIndex = 0)
    {
        _plan = plan;
        _ambientRoot = ambientRoot;
        ZIndex = zIndex;
    }

    /// <summary>Updates active ambient creatures for the camera's view.</summary>
    public void Update(ArtArea view, double clock)
    {
        if (_plan is null)
        {
            return;
        }

        _items = [.. _plan.ActiveAt(clock, view)];
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_items.Length == 0)
        {
            return;
        }

        foreach (var item in _items)
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                continue;
            }

            var fullPath = System.IO.Path.Combine(_ambientRoot, item.Path);
            if (!_textures.TryGetValue(fullPath, out var texture))
            {
                if (System.IO.File.Exists(fullPath))
                {
                    var img = Image.LoadFromFile(fullPath);
                    texture = ImageTexture.CreateFromImage(img);
                    _textures[fullPath] = texture;
                }
            }

            if (texture is not null)
            {
                var srcRect = new Rect2(item.Frame * item.FrameSize.X, 0, item.FrameSize.X, item.FrameSize.Y);
                var corner = new Vector2(
                    item.Position.X - (item.Mirror ? item.FrameSize.X - item.Anchor.X : item.Anchor.X),
                    item.Position.Y - item.Anchor.Y);
                var dstRect = new Rect2(corner, item.Mirror ? new Vector2(-item.FrameSize.X, item.FrameSize.Y) : new Vector2(item.FrameSize.X, item.FrameSize.Y));
                DrawTextureRectRegion(texture, dstRect, srcRect, new Color(1, 1, 1, item.Alpha));
            }
        }
    }
}
