using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>A building of the town kit, under <c>Buildings</c>, on the back corner of its footprint. Its art and footprint are in <c>town/manifest.json</c>.</summary>
[Tool]
[GlobalClass]
public partial class BuildingMarker : TileMarker
{
    private static readonly Color Ink = new(1f, 0.6f, 0.3f);

    private string _building = "";
    private string _variant = "";
    private static readonly Lazy<ArtManifest> Art = new(() => ArtManifests.Load(RegionPaths.Content));

    /// <inheritdoc />
    public override string Group => RegionScene.Buildings;

    /// <inheritdoc />
    protected override bool OnCorner => true;

    /// <summary>The building's id, such as <c>tavern</c>.</summary>
    [Export]
    public string Building
    {
        get => _building;
        set
        {
            _building = value;
            QueueRedraw();
        }
    }

    /// <summary>Its variant, such as <c>red</c>.</summary>
    [Export]
    public string Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        var origin = PlaceOn(Tile) - Position;
        if (Building.Length > 0 && Art.Value.Buildings.TryGetValue(Building, out var art))
        {
            var size = new Vector2(art.Width, art.Depth) * PaintedRegion.TilePixels;
            DrawRect(new Rect2(origin, size), Ink, filled: false, width: 2);
            for (var x = 1; x < art.Width; x++)
                DrawLine(origin + new Vector2(x * PaintedRegion.TilePixels, 0), origin + new Vector2(x * PaintedRegion.TilePixels, size.Y), Ink with { A = 0.45f });
            for (var y = 1; y < art.Depth; y++)
                DrawLine(origin + new Vector2(0, y * PaintedRegion.TilePixels), origin + new Vector2(size.X, y * PaintedRegion.TilePixels), Ink with { A = 0.45f });
        }
        else
        {
            DrawTile(Ink);
        }
        DrawString(ThemeDB.FallbackFont, origin + new Vector2(4, -4), Building.Length > 0 ? Building : "(no Building id)", modulate: Ink);
    }
}
