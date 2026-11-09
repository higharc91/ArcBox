using ArcBox.Host.Shell;
using Godot;
using Tactics.Presentation.Beats;

namespace ArcBox.Host.Games.Tactics;

/// <summary>TV-only presentation preferences; the phone and game rules are unchanged.</summary>
public partial class BattlePresentationControls : Control
{
    private readonly PanelContainer _panel;
    public BattlePresentationControls() : this(null, false, (_, _) => { }) { }
    public BattlePresentationControls(BattleCinematics? cinema, bool reduced, Action<BattleCinematics?, bool> changed)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var mode = new OptionButton();
        foreach (var name in new[] { "Auto", "Full", "Short", "Off" }) mode.AddItem(name);
        mode.Selected = cinema is null ? 0 : (int)cinema.Value + 1;
        var effects = new OptionButton();
        effects.AddItem("Full"); effects.AddItem("Reduced"); effects.Selected = reduced ? 1 : 0;
        void Apply() => changed(mode.Selected == 0 ? null : (BattleCinematics)(mode.Selected - 1), effects.Selected == 1);
        mode.ItemSelected += _ => Apply();
        effects.ItemSelected += _ => Apply();
        _panel = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 20, 16), ArcTheme.VBox(8,
            TvText.Label("Battle cinematics", ArcTheme.Bold, 22), mode,
            TvText.Label("Auto uses Short for 5–6 players. Full is used otherwise.", ArcTheme.SemiBold, 16),
            TvText.Label("Effects", ArcTheme.Bold, 22), effects,
            TvText.Label("Reduced removes flashes and shake, and shortens effects.", ArcTheme.SemiBold, 16)));
        _panel.Position = new Vector2(1260, 730);
        _panel.Visible = false;
        AddChild(_panel);
        var button = new Button { Text = "Battle settings · F6", Position = new Vector2(1630, 1028), CustomMinimumSize = new Vector2(250, 40) };
        button.Pressed += Toggle;
        AddChild(button);
    }
    public void Toggle() => _panel.Visible = !_panel.Visible;
}
