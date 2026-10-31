using Godot;

namespace Tactics.World.Editor;

/// <summary>A beat spot, under <c>Beats</c>: where a beat in <c>lore/arcs.json</c> happens. It stands on the tile its position falls in.</summary>
[Tool]
[GlobalClass]
public partial class BeatMarker : Marker2D
{
    private static readonly Color Ink = new(1f, 0.9f, 0.4f);

    private string _beat = "";

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
        if (Engine.IsEditorHint())
            DrawString(ThemeDB.FallbackFont, new Vector2(10, -6), Beat, modulate: Ink);
    }
}
