using Godot;

namespace Tactics.World.Editor;

/// <summary>A building of the town kit, under <c>Buildings</c>, on the back corner of its footprint. Its art and footprint are in <c>town/manifest.json</c>.</summary>
[Tool]
[GlobalClass]
public partial class BuildingMarker : TileMarker
{
    private static readonly Color Ink = new(1f, 0.6f, 0.3f);

    private string _building = "";
    private string _variant = "";

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
        DrawTile(Ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(4, -4), Building.Length > 0 ? Building : "(no Building id)", modulate: Ink);
    }
}
