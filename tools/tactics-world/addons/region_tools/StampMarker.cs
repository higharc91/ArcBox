using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// A battle map stamped into the world, under <c>Stamps</c>, standing on the tile of the map's corner and drawn as an outline over the
/// map's tiles. Those tiles must stay exactly as the map has them, so don't paint inside the outline: the export refuses a stamp that
/// doesn't match. To place or move one, put the marker where it should be and press Apply stamp, which writes the map to the layers.
/// Until then the outline is orange.
/// </summary>
[Tool]
[GlobalClass]
public partial class StampMarker : TileMarker
{
    /// <summary>The value of <see cref="AppliedAt"/> for a stamp whose tiles were never written.</summary>
    public static readonly Vector2I NotApplied = new(-1, -1);

    private static readonly Color Outline = new(1f, 0.3f, 0.3f), Pending = new(1f, 0.6f, 0.1f);

    private string _map = "";
    private int _heightOffset;
    private Vector2I _footprint;
    private Vector2I _appliedAt = NotApplied;

    /// <inheritdoc />
    public override string Group => RegionScene.Stamps;

    /// <summary>The battle map's id.</summary>
    [Export]
    public string Map
    {
        get => _map;
        set
        {
            _map = value;
            _footprint = StampMaps.Read(RegionPaths.Rules, value) is { } map ? new Vector2I(map.Width, map.Height) : Vector2I.Zero;
            QueueRedraw();
        }
    }

    /// <summary>What's added to the map's heights in the world.</summary>
    [Export]
    public int HeightOffset
    {
        get => _heightOffset;
        set
        {
            _heightOffset = value;
            QueueRedraw();
        }
    }

    /// <summary>Where Apply stamp last wrote this stamp's tiles, or <see cref="NotApplied"/>.</summary>
    [Export]
    public Vector2I AppliedAt
    {
        get => _appliedAt;
        set
        {
            _appliedAt = value;
            QueueRedraw();
        }
    }

    /// <summary>The tile the stamp's tiles are at now, if it has any.</summary>
    public Tile? AppliedTile => AppliedAt == NotApplied ? null : new Tile(AppliedAt.X, AppliedAt.Y);

    /// <inheritdoc />
    protected override bool OnCorner => true;

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        var stale = AppliedTile != Tile;
        var ink = stale ? Pending : Outline;
        var corner = PlaceOn(Tile) - Position;
        DrawRect(new Rect2(corner, _footprint * PaintedRegion.TilePixels), ink, filled: false, width: 3);
        DrawTile(ink);
        var label = _footprint == Vector2I.Zero ? $"unknown map '{Map}'" : $"{Map} (height {HeightOffset:+0;-0;+0})";
        DrawString(ThemeDB.FallbackFont, corner + new Vector2(4, -6), stale ? $"{label}: press Apply stamp" : label, modulate: ink);
    }
}
