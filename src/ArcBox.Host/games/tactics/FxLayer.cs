using System.Collections.Immutable;
using Godot;
using Tactics.Presentation.Beats;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// One blend of a beat's effects, numbers and badges, over the whole board. A canvas item has one blend mode for all it
/// draws, so <see cref="BoardCanvas"/> has one of these for light and one for everything else. What it draws is worked out in
/// <see cref="BeatPlayer"/>.
/// </summary>
public partial class FxLayer : Node2D
{
    private readonly Func<string?, Texture2D?> _load;
    private readonly bool _additive;
    private ImmutableArray<FxDraw> _draws = [];

    public FxLayer()
    {
        _load = _ => null;
    }

    public FxLayer(Func<string?, Texture2D?> load, bool additive)
    {
        _load = load;
        _additive = additive;
        if (additive)
        {
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        }
    }

    /// <summary>Shows the draws of <paramref name="draws"/> in this layer's blend.</summary>
    public void Show(ImmutableArray<FxDraw> draws)
    {
        var mine = draws.Where(d => d.Additive == _additive).ToImmutableArray();
        if (!mine.IsEmpty || !_draws.IsEmpty)
        {
            _draws = mine;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        foreach (var draw in _draws)
        {
            if (_load(draw.Path) is not { } texture)
            {
                continue;
            }
            var size = new Vector2(draw.Size.X, draw.Size.Y);
            var source = new Rect2(draw.Source.X, draw.Source.Y, draw.Source.Width, draw.Source.Height);
            DrawSetTransform(new Vector2(draw.TopLeft.X, draw.TopLeft.Y) + size / 2, draw.Rotation);
            DrawTextureRectRegion(texture, new Rect2(-size / 2, size), source, new Color(1, 1, 1, draw.Alpha));
        }
    }
}
