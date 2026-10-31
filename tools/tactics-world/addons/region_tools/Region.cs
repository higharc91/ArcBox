using Godot;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The root of a region scene. Its children are the <c>Material</c>, <c>Height</c> and <c>Flags</c> tile layers on one flat grid,
/// and the <c>Beats</c>, <c>Stamps</c> and <c>Spawns</c> nodes holding markers. See <see cref="RegionScene"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Region : Node2D
{
    private static readonly Color Edge = new(1f, 0.85f, 0.2f);

    private Vector2I _size = new(32, 32);

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

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Engine.IsEditorHint())
            DrawRect(new Rect2(Vector2.Zero, Size * PaintedRegion.TilePixels), Edge, filled: false, width: 3);
    }
}
