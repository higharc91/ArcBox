using ArcBox.Host.Core;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>Server URL field and connection status. Retries are driven by <c>Main</c>; this only shows them.</summary>
public partial class ConnectScreen : Control
{
    private LineEdit _url = null!;
    private Label _status = null!;
    private Label _detail = null!;
    private ColorRect _dot = null!;
    private bool _pulse;
    private double _t;
    private string _initialUrl = HostSettings.DefaultServer;

    /// <summary>The user pressed Connect / Enter with this URL.</summary>
    public event Action<string>? ConnectRequested;

    public string ServerUrl
    {
        get => _url?.Text.Trim() ?? _initialUrl;
        set
        {
            _initialUrl = value;
            if (_url is not null)
            {
                _url.Text = value;
            }
        }
    }

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this);

        column.AddChild(ShellLayout.Header(
            "ARCBOX",
            ArcTheme.Meta("TV host · party games"),
            ArcTheme.Label("Connecting the TV to the game server", ArcTheme.Bold, 40)));

        column.AddChild(ArcTheme.Spacer(false, true));

        // Centre card.
        _url = new LineEdit
        {
            Text = _initialUrl,
            PlaceholderText = HostSettings.DefaultServer,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 72),
            SelectAllOnFocus = true,
        };
        _url.TextSubmitted += text => ConnectRequested?.Invoke(text.Trim());
        var connect = new Button { Text = "CONNECT", CustomMinimumSize = new Vector2(220, 72) };
        connect.AddThemeStyleboxOverride("normal", ArcTheme.Flat(ArcTheme.Red, 0));
        connect.AddThemeStyleboxOverride("hover", ArcTheme.Flat(ArcTheme.Red.Lightened(0.12f), 0));
        connect.AddThemeFontOverride("font", ArcTheme.BlackItalic);
        connect.AddThemeFontSizeOverride("font_size", 30);
        connect.Pressed += () => ConnectRequested?.Invoke(ServerUrl);

        _dot = new ColorRect { Color = ArcTheme.Dim, CustomMinimumSize = new Vector2(18, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _status = ArcTheme.Label("Starting…", ArcTheme.Bold, 38);
        _detail = ArcTheme.Label("", ArcTheme.SemiBold, 24, ArcTheme.Dim);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var card = ArcTheme.VBox(18,
            ArcTheme.Meta("Server"),
            ArcTheme.HBox(14, _url, connect),
            ArcTheme.Margin(0, 18, 0, 0, ArcTheme.HBox(18, _dot, _status)),
            _detail);
        var panel = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Red, 48, 40), card);
        panel.CustomMinimumSize = new Vector2(1100, 0);

        var centre = new CenterContainer();
        centre.AddChild(panel);
        column.AddChild(centre);

        column.AddChild(ArcTheme.Spacer(false, true));
        var foot = ArcTheme.Label("Phones join from the same server · the room code appears once connected", ArcTheme.SemiBold, 22, ArcTheme.Dim);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(foot);
    }

    /// <summary>Connecting / failed / connected. <paramref name="busy"/> pulses the dot.</summary>
    public void SetStatus(string status, string detail, Color dot, bool busy)
    {
        _status.Text = status;
        _detail.Text = detail;
        _dot.Color = dot;
        _pulse = busy;
        if (!busy)
        {
            _dot.Modulate = Colors.White;
        }
    }

    public override void _Process(double delta)
    {
        if (_pulse)
        {
            _t += delta;
            _dot.Modulate = new Color(1, 1, 1, 0.35f + (0.65f * (float)((Math.Sin(_t * 5) + 1) / 2)));
        }
    }
}
