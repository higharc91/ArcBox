using ArcBox.Host.Shell;
using Godot;
using Tactics.Game;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>A unit's displayed state, including the numbers and names that colour and icons cannot convey.</summary>
public sealed partial class UnitCard : PanelContainer
{
    private readonly Label _heading;
    private readonly Label _name;
    private readonly Label _health;
    private readonly Label _role;
    private readonly Label _conditions;
    private readonly StoryFace _face;
    private readonly StoryArt _art;

    public UnitCard(StoryArt art, string heading)
    {
        _art = art;
        AddThemeStyleboxOverride("panel", ArcTheme.GlassBox(HudLayer.Gold, 18, 12));
        CustomMinimumSize = new Vector2(480, 0);
        _heading = ArcTheme.Meta(heading, 20, HudLayer.Gold);
        _name = ArcTheme.Label("", ArcTheme.Bold, 28, HudLayer.Ink);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _role = ArcTheme.Label("", ArcTheme.SemiBold, 20, HudLayer.Muted);
        _role.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _health = ArcTheme.Label("", ArcTheme.Bold, 26, HudLayer.Ink);
        _health.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _conditions = ArcTheme.Label("", ArcTheme.SemiBold, 24, HudLayer.Ink);
        _conditions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var words = ArcTheme.VBox(4, _heading, _name, _role, _health, _conditions);
        words.CustomMinimumSize = new Vector2(340, 0);
        words.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _face = new StoryFace(art, new Vector2(80, 80));
        _face.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(ArcTheme.HBox(12, _face, words));
        Visible = false;
    }

    public void Show(UnitView? unit)
    {
        Visible = unit is not null;
        if (unit is null) return;
        var tone = unit.Side == "Enemy" ? UnitTone.Enemy : unit.Side == "Party" ? UnitTone.Party : UnitTone.Neutral;
        _face.Show(_art.Hud.Portrait(unit.Sprite, tone));
        _name.Text = unit.Name;
        _role.Text = unit.Side + (unit.Role is { } role ? " · " + role : "") + (unit.BaseAc is { } ac ? $" · Base AC {ac}" : "");
        _health.Text = $"HP {unit.Hp}/{unit.MaxHp}" + (unit.TempHp > 0 ? $" +{unit.TempHp} temporary" : "");
        var conditions = unit.Conditions.Select(global::Tactics.Presentation.Words.Title).ToList();
        if (unit.Life != "Up") conditions.Insert(0, unit.Life);
        else if (unit.Hp * 2 <= unit.MaxHp) conditions.Insert(0, "Bloodied");
        _conditions.Text = conditions.Count == 0 ? "No conditions" : string.Join(" · ", conditions);
        Size = new Vector2(480, GetCombinedMinimumSize().Y);
    }
}
