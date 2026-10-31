using System.Text.Json;
using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// A battle map stamped into the world, under <c>Stamps</c>, drawn as an outline over its tiles. It stands on the tile of the map's
/// corner. Its tiles must stay exactly as the map has them, so the export refuses a stamp that was painted over, and the marker
/// locks itself in the editor: moving it without its tiles can only fail.
/// </summary>
[Tool]
[GlobalClass]
public partial class StampMarker : Marker2D
{
    private static readonly Color Outline = new(1f, 0.3f, 0.3f);

    private string _map = "";
    private int _heightOffset;
    private Vector2I _footprint;

    /// <summary>The battle map's id.</summary>
    [Export]
    public string Map
    {
        get => _map;
        set
        {
            _map = value;
            _footprint = MapSize(value);
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

    /// <inheritdoc />
    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            SetMeta("_edit_lock_", true);
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        var tile = PaintedRegion.TileAt(Position.X, Position.Y);
        var corner = new Vector2(tile.X, tile.Y) * PaintedRegion.TilePixels - Position;
        DrawRect(new Rect2(corner, _footprint * PaintedRegion.TilePixels), Outline, filled: false, width: 3);
        var label = _footprint == Vector2I.Zero ? $"unknown map '{Map}'" : $"{Map} (height {HeightOffset:+0;-0;+0})";
        DrawString(ThemeDB.FallbackFont, corner + new Vector2(4, -6), label, modulate: Outline);
    }

    /// <summary>The map's size in tiles, or zero if the pack has no such map.</summary>
    private static Vector2I MapSize(string map)
    {
        var path = Path.Combine(RegionPaths.Rules, "maps", $"{map}.json");
        if (map.Length == 0 || !File.Exists(path))
            return Vector2I.Zero;
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var size = json.RootElement.GetProperty("size");
        return new Vector2I(size[0].GetInt32(), size[1].GetInt32());
    }
}
