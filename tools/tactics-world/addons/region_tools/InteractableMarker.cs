using Godot;
namespace Tactics.World.Editor;
[Tool, GlobalClass]
public partial class InteractableMarker : TileMarker
{
    [Export] public string Id { get; set; } = "";
    public override string Group => Placements.Things;
    public override void _Draw()
    {
        if (!Engine.IsEditorHint()) return;
        var ink = new Color(0.9f, 0.7f, 0.3f);
        DrawTile(ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(-14, -18), Id, modulate: ink);
    }
}
