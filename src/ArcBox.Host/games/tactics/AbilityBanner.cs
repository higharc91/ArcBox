using ArcBox.Host.Shell;
using Godot;

namespace ArcBox.Host.Games.Tactics;

/// <summary>Shared ability name banner for cut-ins and the TV roll-panel work (#164).</summary>
public partial class AbilityBanner : PanelContainer
{
    private readonly Label _name;

    public AbilityBanner()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(620, 84);
        CustomMinimumSize = new Vector2(680, 0);
        AddThemeStyleboxOverride("panel", ArcTheme.GlassBox(ArcTheme.Gold, 24, 10));
        _name = TvText.Label("", ArcTheme.Bold, 28, ArcTheme.Gold);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_name);
        Visible = false;
    }

    public void ShowName(string? name)
    {
        Visible = name is not null;
        _name.Text = name ?? "";
    }
}
