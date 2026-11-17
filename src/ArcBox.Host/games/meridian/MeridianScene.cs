using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Meridian.Game;
using Meridian.Presentation;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// Derelict: ISV Meridian's TV scene (spec §5.5). It only animates: every rule and outcome is decided by the game, and
/// every decision about what to show is made by <c>Meridian.Presentation</c>, which this scene draws (M4 plan). Stage
/// M4 grows it task by task; this is the skeleton the shell can already show.
/// </summary>
public partial class MeridianScene : Control, IGameScene
{
    private Label _banner = null!;
    private Label _mode = null!;
    private HBoxContainer _seats = null!;
    private Screenshotter? _shots;

    public void SetShots(Screenshotter? shots) => _shots = shots;

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);
        _banner = ArcTheme.Label("", ArcTheme.Bold, 42);
        _mode = ArcTheme.Meta("", 22);
        column.AddChild(ShellLayout.Header("DERELICT: ISV MERIDIAN", _mode, _banner, ArcTheme.Spacer()));
        _seats = ArcTheme.HBox(16);
        column.AddChild(_seats);
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        if (view.Deserialize<MeridianHostView>(ArcJson.Options) is not { } v)
        {
            return;
        }
        var screen = TvScreen.Of(v);
        _banner.Text = screen.Banner;
        _mode.Text = screen.Mode.ToString().ToUpperInvariant();
        foreach (var child in _seats.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var seat in screen.Seats)
        {
            var text = seat.Crew is { } crew ? $"{seat.Name} · {crew}" : seat.Name;
            _seats.AddChild(ArcTheme.Label(seat.Active ? $"▶ {text}" : text, ArcTheme.Bold, 28));
        }
    }
}
