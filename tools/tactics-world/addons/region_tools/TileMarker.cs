using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>A marker that stands on one tile. In the editor it snaps to its tile as it's dragged, and draws the tile it stands on.</summary>
[Tool]
public abstract partial class TileMarker : Marker2D
{
    private bool _snapping;

    /// <summary>The tile it stands on.</summary>
    public Tile Tile => PaintedRegion.TileAt(Position.X, Position.Y);

    /// <summary>The node this kind of marker belongs under.</summary>
    public abstract string Group { get; }

    /// <summary>Whether it sits on its tile's corner, as a stamp's outline needs, rather than its centre.</summary>
    protected virtual bool OnCorner => false;

    /// <summary>Where a marker on <paramref name="tile"/> sits.</summary>
    public Vector2 PlaceOn(Tile tile)
    {
        var corner = new Vector2(tile.X, tile.Y) * PaintedRegion.TilePixels;
        return OnCorner ? corner : corner + (Vector2.One * PaintedRegion.TilePixels / 2);
    }

    /// <inheritdoc />
    public override void _EnterTree()
    {
        if (Engine.IsEditorHint())
            SetNotifyLocalTransform(true);
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what != NotificationLocalTransformChanged || _snapping || !Engine.IsEditorHint())
            return;
        _snapping = true;
        Position = PlaceOn(Tile);
        _snapping = false;
        QueueRedraw();
    }

    /// <summary>Outlines its tile.</summary>
    protected void DrawTile(Color ink)
        => DrawRect(new Rect2(PlaceOn(Tile) - (OnCorner ? Vector2.Zero : Vector2.One * PaintedRegion.TilePixels / 2) - Position,
            Vector2.One * PaintedRegion.TilePixels), ink, filled: false, width: 2);
}
