using Godot;

namespace Tactics.World.Editor;

/// <summary>A beat spot, under <c>Beats</c>: where a beat in <c>lore/arcs.json</c> happens.</summary>
[Tool]
[GlobalClass]
public partial class BeatMarker : TileMarker
{
    private static readonly Color Ink = new(1f, 0.9f, 0.4f);

    private string _beat = "";

    /// <inheritdoc />
    public override string Group => RegionScene.Beats;

    /// <summary>The beat's id, such as <c>ch1_old_maud</c>.</summary>
    [Export]
    public string Beat
    {
        get => _beat;
        set
        {
            _beat = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;
        DrawTile(Ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(18, -6), Beat.Length > 0 ? Beat : "(no Beat id)", modulate: Ink);
    }
}
