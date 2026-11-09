using Godot;
using Tactics.WorldTools;
namespace Tactics.World.Editor;
[Tool, GlobalClass]
public partial class MobPatchMarker : TileMarker
{
    [Export] public string Id { get; set; } = "";
    [Export] public string Offsets { get; set; } = "0,0";
    public override string Group => Placements.Mobs;
    public override void _Draw()
    {
        if (!Engine.IsEditorHint()) return;
        var ink = new Color(1, 0.35f, 0.3f);
        DrawTile(ink);
        foreach (var offset in PersonText.Walk(Offsets).IsDefault ? [] : PersonText.Walk(Offsets))
            DrawRect(new Rect2(new Vector2(offset.X, offset.Y) * PaintedRegion.TilePixels - Vector2.One * PaintedRegion.TilePixels / 2,
                Vector2.One * PaintedRegion.TilePixels), ink, filled: false, width: 1);
        DrawString(ThemeDB.FallbackFont, new Vector2(-14, -18), Id, modulate: ink);
    }
}
