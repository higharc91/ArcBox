using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// A battle map stamped into the world, under <c>Stamps</c>, standing on the tile of the map's corner and drawn as an outline over the
/// map's tiles. Those tiles must stay exactly as the map has them, so don't paint inside the outline: the export refuses a stamp that
/// doesn't match. To place or move one, or change its map or height, set the marker and press Apply stamp, which writes the map to the
/// layers. Until then the outline is orange. A stamp moved away leaves its old tiles behind, so the marker keeps a list of those areas
/// and draws them until the painter says they're repainted.
/// </summary>
[Tool]
[GlobalClass]
public partial class StampMarker : TileMarker
{
    /// <summary>The value of <see cref="AppliedAt"/> for a stamp whose tiles were never written.</summary>
    public static readonly Vector2I NotApplied = new(-1, -1);

    private static readonly Color Outline = new(1f, 0.3f, 0.3f), Waiting = new(1f, 0.6f, 0.1f);

    private string _map = "";
    private int _heightOffset;
    private Vector2I _footprint;
    private Vector2I _appliedAt = NotApplied;
    private string _appliedMap = "";
    private int _appliedHeightOffset;
    private string[] _pendingAreas = [];

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
            _footprint = Footprint(value);
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

    /// <summary>The map Apply stamp last wrote.</summary>
    [Export]
    public string AppliedMap
    {
        get => _appliedMap;
        set
        {
            _appliedMap = value;
            QueueRedraw();
        }
    }

    /// <summary>The height offset Apply stamp last wrote.</summary>
    [Export]
    public int AppliedHeightOffset
    {
        get => _appliedHeightOffset;
        set
        {
            _appliedHeightOffset = value;
            QueueRedraw();
        }
    }

    /// <summary>The old areas waiting to be repainted, each as <see cref="StampBrush.Encode"/> wrote it.</summary>
    [Export]
    public string[] PendingAreas
    {
        get => _pendingAreas;
        set
        {
            _pendingAreas = value;
            QueueRedraw();
        }
    }

    /// <summary>What the marker asks for now.</summary>
    public RegionStamp Current => new(Map, Tile, HeightOffset);

    /// <summary>What Apply stamp last wrote, if it has.</summary>
    public RegionStamp? Applied => AppliedAt == NotApplied ? null : new RegionStamp(AppliedMap, new Tile(AppliedAt.X, AppliedAt.Y), AppliedHeightOffset);

    /// <summary>The old areas waiting to be repainted.</summary>
    public IReadOnlyList<RegionStamp> Pending => [.. PendingAreas.Select(StampBrush.Decode).OfType<RegionStamp>()];

    /// <inheritdoc />
    protected override bool OnCorner => true;

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        var stale = Applied != Current;
        var ink = stale ? Waiting : Outline;
        var corner = PlaceOn(Tile) - Position;
        DrawRect(new Rect2(corner, _footprint * PaintedRegion.TilePixels), ink, filled: false, width: 3);
        DrawTile(ink);
        var label = _footprint == Vector2I.Zero ? $"unknown map '{Map}'" : $"{Map} (height {HeightOffset:+0;-0;+0})";
        DrawString(ThemeDB.FallbackFont, corner + new Vector2(4, -6), stale ? $"{label}: press Apply stamp" : label, modulate: ink);

        foreach (var area in Pending)
        {
            var at = (new Vector2(area.At.X, area.At.Y) * PaintedRegion.TilePixels) - Position;
            DrawRect(new Rect2(at, Footprint(area.Map) * PaintedRegion.TilePixels), Waiting, filled: false, width: 1);
            DrawString(ThemeDB.FallbackFont, at + new Vector2(4, 14), $"old {area.Map}: repaint", modulate: Waiting);
        }
    }

    private static Vector2I Footprint(string map)
        => StampMaps.Read(RegionPaths.Rules, map) is { } found ? new Vector2I(found.Width, found.Height) : Vector2I.Zero;
}
