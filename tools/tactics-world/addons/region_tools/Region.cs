using Godot;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The root of a region scene. Its children are the <c>Material</c>, <c>Height</c> and <c>Flags</c> tile layers on one flat grid,
/// and the <c>Beats</c>, <c>Stamps</c>, <c>Spawns</c> and <c>Buildings</c> nodes holding markers. See <see cref="RegionScene"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Region : Node2D
{
    private static readonly Color Edge = new(1f, 0.85f, 0.2f), Mark = new(0f, 1f, 1f);

    private Vector2I _size = new(32, 32);
    private Vector2I? _highlight;

    /// <summary>The region's id, which names its file: <c>rules/world/&lt;id&gt;.json</c>.</summary>
    [Export]
    public string Id { get; set; } = "";

    /// <summary>The region's name as players see it. It hides the node's own name, which a region scene doesn't use.</summary>
    [Export]
    public new string Name { get; set; } = "";

    /// <summary>The chapter the region belongs to.</summary>
    [Export]
    public int Chapter { get; set; } = 1;

    /// <summary>The region's size in tiles. Anything painted outside it is refused on export.</summary>
    [Export]
    public Vector2I Size
    {
        get => _size;
        set
        {
            _size = value;
            QueueRedraw();
        }
    }

    /// <summary>The tile a clicked problem points at, drawn over the paint. It isn't saved.</summary>
    public Vector2I? Highlight
    {
        get => _highlight;
        set
        {
            _highlight = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        DrawRect(new Rect2(Vector2.Zero, Size * PaintedRegion.TilePixels), Edge, filled: false, width: 3);
        if (Highlight is { } tile)
            DrawRect(new Rect2(tile * PaintedRegion.TilePixels, Vector2.One * PaintedRegion.TilePixels), Mark, filled: false, width: 4);
    }
}
