using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// Shown instead of a game's scene while its content packs download (spec §3.6): "Loading content… 42%" with a bar, or
/// "Couldn't load game content" with Retry (click, or Enter). <see cref="Main"/> moves it on from the
/// <see cref="Core.ContentLoad"/>.
/// </summary>
public partial class ContentLoadingCard : Control
{
    private string _gameName = "";
    private int _percent;
    private bool _failed;

    private Label _title = null!;
    private Label _status = null!;
    private Label _detail = null!;
    private ColorRect _fill = null!;
    private Control _bar = null!;
    private Button _retry = null!;

    /// <summary>Retry was pressed after a failure.</summary>
    public event Action? RetryPressed;

    public void Setup(string gameName) => _gameName = gameName;

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);
        _title = ArcTheme.Label("Getting the game ready", ArcTheme.Bold, 40);
        column.AddChild(ShellLayout.Header(
            string.IsNullOrEmpty(_gameName) ? "ARCBOX" : _gameName.ToUpperInvariant(),
            ArcTheme.Meta("Game content"),
            _title));

        _status = ArcTheme.Label("", ArcTheme.BlackItalic, 54);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _detail = ArcTheme.Label("", ArcTheme.SemiBold, 22, ArcTheme.Dim);
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        (_bar, _fill) = ShellLayout.Bar(8);
        _bar.CustomMinimumSize = new Vector2(720, 8);
        _bar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _retry = new Button { Text = "RETRY", CustomMinimumSize = new Vector2(220, 72), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _retry.Pressed += Retry;

        var content = ArcTheme.VBox(28, _status, _bar, _detail, _retry);
        content.Alignment = BoxContainer.AlignmentMode.Center;
        var card = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Red, 64, 48), content);
        card.CustomMinimumSize = new Vector2(1000, 0);
        card.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        card.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var middle = ArcTheme.VBox(0, card);
        middle.SizeFlagsVertical = SizeFlags.ExpandFill;
        middle.Alignment = BoxContainer.AlignmentMode.Center;
        column.AddChild(middle);
        Apply();
    }

    /// <summary>The average across the game's packs, 0–100.</summary>
    public void ShowProgress(int percent)
    {
        _failed = false;
        _percent = Math.Clamp(percent, 0, 100);
        if (IsNodeReady())
        {
            Apply();
        }
    }

    /// <summary>A pack couldn't be downloaded or verified: offer Retry.</summary>
    public void ShowFailed()
    {
        _failed = true;
        if (IsNodeReady())
        {
            Apply();
            _retry.GrabFocus();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_failed && e.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            Retry();
        }
    }

    private void Retry()
    {
        if (_failed)
        {
            RetryPressed?.Invoke();
        }
    }

    private void Apply()
    {
        _status.Text = _failed ? "Couldn't load game content" : $"Loading content… {_percent}%";
        _status.AddThemeColorOverride("font_color", _failed ? ArcTheme.Bad : Colors.White);
        _detail.Text = _failed
            ? "Check the TV's connection to the server, then retry."
            : "Downloading this game's content from the server.";
        _bar.Visible = !_failed;
        _retry.Visible = _failed;
        ShellLayout.SetBar(_fill, _percent / 100f);
    }
}
