using Godot;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>Someone the TV draws, under <c>People</c>, on the tile they stand on. Their identity and gates support server interactions; they never block paths.</summary>
[Tool]
[GlobalClass]
public partial class PersonMarker : TileMarker
{
    [Export] public string ShopId { get; set; } = "";
    [Export] public string Id { get; set; } = "";
    [Export] public string Character { get; set; } = "";
    [Export] public string Requires { get; set; } = "";
    [Export] public string Unless { get; set; } = "";
    [Export] public string Bark { get; set; } = "";
    /// <summary>Optional art suffix: farmer + b selects characters.farmer_b when available.</summary>
    [Export] public string Variant { get; set; } = "";
    [Export] public string Voice { get; set; } = "";
    private static readonly Color Ink = new(0.45f, 1f, 0.6f);

    private string _preset = "";
    private string _face = "sw";
    private string _walk = "";

    /// <inheritdoc />
    public override string Group => RegionScene.People;

    /// <summary>Who they are: a preset in <c>characters/presets</c>, such as <c>guard</c>.</summary>
    [Export]
    public string Preset
    {
        get => _preset;
        set
        {
            _preset = value;
            QueueRedraw();
        }
    }

    /// <summary>Which way they look on the screen: <c>se</c>, <c>sw</c>, <c>nw</c> or <c>ne</c>.</summary>
    [Export(PropertyHint.Enum, "se,sw,nw,ne")]
    public string Face
    {
        get => _face;
        set
        {
            _face = value;
            QueueRedraw();
        }
    }

    /// <summary>The tiles of a short loop they walk, as <c>x,y x,y</c>, a step at a time and back to where they stand. Blank to stand still.</summary>
    [Export]
    public string Walk
    {
        get => _walk;
        set
        {
            _walk = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        DrawTile(Ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(-14, -18), ShopId.Length > 0 ? $"Shop: {ShopId}" : Preset.Length > 0 ? Preset : "(no Preset)", modulate: Ink);

        // Its walk, so a loop can be seen and put right where it's painted.
        var from = Vector2.Zero;
        foreach (var tile in PersonText.Walk(Walk).IsDefault ? [] : PersonText.Walk(Walk))
        {
            var to = PlaceOn(tile) - Position;
            DrawLine(from, to, Ink, 2);
            from = to;
        }
        if (from != Vector2.Zero)
            DrawLine(from, Vector2.Zero, Ink, 2);
    }
}
