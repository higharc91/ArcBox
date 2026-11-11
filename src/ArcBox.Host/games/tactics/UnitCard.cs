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
    private UnitView? _shown;

    public UnitCard(StoryArt art, string heading)
    {
        _art = art;
        AddThemeStyleboxOverride("panel", ArcTheme.GlassBox(HudLayer.Gold, 18, 12));
        CustomMinimumSize = new Vector2(480, 0);
        _heading = ArcTheme.Meta(heading, 20, HudLayer.Gold);
        _name = ArcTheme.Label("", ArcTheme.Bold, 28, HudLayer.Ink);
        _role = ArcTheme.Label("", ArcTheme.SemiBold, 20, HudLayer.Muted);
        _health = ArcTheme.Label("", ArcTheme.Bold, 26, HudLayer.Ink);
        _conditions = ArcTheme.Label("", ArcTheme.SemiBold, 24, HudLayer.Ink);
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
        if (unit == _shown) return;
        _shown = unit;
        if (unit is null) return;
        var tone = unit.Side == "Enemy" ? UnitTone.Enemy : unit.Side == "Party" ? UnitTone.Party : UnitTone.Neutral;
        _face.Show(_art.Hud.Portrait(unit.Sprite, tone));
        TvText.SetWrappedText(_name, unit.Name, 340);
        TvText.SetWrappedText(_role, unit.Side + (unit.Role is { } role ? " · " + role : "") + (unit.BaseAc is { } ac ? $" · Base AC {ac}" : ""), 340);
        TvText.SetWrappedText(_health, $"HP {unit.Hp}/{unit.MaxHp}" + (unit.TempHp > 0 ? $" +{unit.TempHp} temporary" : ""), 340);
        var conditions = unit.Conditions.Select(global::Tactics.Presentation.Words.Title).ToList();
        if (unit.Life != "Up") conditions.Insert(0, unit.Life);
        else if (unit.Hp * 2 <= unit.MaxHp) conditions.Insert(0, "Bloodied");
        TvText.SetWrappedText(_conditions, conditions.Count == 0 ? "No conditions" : string.Join(" · ", conditions), 340);
        Size = new Vector2(480, GetCombinedMinimumSize().Y);
    }
}
