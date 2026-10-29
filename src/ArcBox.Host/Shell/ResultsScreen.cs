using ArcBox.Host.Core;
using ArcBox.Sdk;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>Final scores after <c>GameEnded</c>. Holds for <see cref="HoldSeconds"/> or until a key press.</summary>
public partial class ResultsScreen : Control
{
    public const double HoldSeconds = 10;

    private GameResults _results = new([]);
    private string _gameName = "";
    private IReadOnlyList<PlayerId> _joinOrder = [];
    private double _shownAt;
    private Label _countdown = null!;
    private ColorRect _bar = null!;

    /// <summary>The hold is over or a key was pressed; <c>Main</c> decides whether the lobby is ready.</summary>
    public event Action? Dismissed;

    public bool HoldElapsed => Elapsed >= HoldSeconds;

    private double Elapsed => (Time.GetTicksMsec() / 1000.0) - _shownAt;

    /// <param name="joinOrder">Lobby player order, so each row keeps the colour the player had in the lobby.</param>
    public void Setup(GameResults results, string gameName, IReadOnlyList<PlayerId> joinOrder)
    {
        _results = results;
        _gameName = gameName;
        _joinOrder = joinOrder;
    }

    public override void _Ready()
    {
        _shownAt = Time.GetTicksMsec() / 1000.0;
        var column = ShellLayout.Frame(this, 24);

        var winner = _results.Scores.FirstOrDefault();
        var title = _results.Message is { Length: > 0 } msg
            ? msg
            : winner is null ? "No scores this time" : $"{winner.Name} wins!";
        var meta = ArcTheme.Meta(string.IsNullOrEmpty(_gameName) ? "Game over" : $"Game over · {_gameName}");
        column.AddChild(ShellLayout.Header("RESULTS", meta, ArcTheme.Label(title, ArcTheme.Bold, 44)));

        var rows = ArcTheme.VBox(0);
        rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        var head = ArcTheme.HBox(0, ArcTheme.Meta("Pos · Player"), ArcTheme.Spacer(), ArcTheme.Meta("Score"));
        var headPanel = ArcTheme.Panel(LobbyScreen.BottomEdge(Colors.Transparent, 28, 12), head);

        if (_results.Scores.Count == 0)
        {
            var none = ArcTheme.Label(_results.Message ?? "Nobody scored.", ArcTheme.SemiBold, 34, ArcTheme.Dim);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            none.SizeFlagsVertical = SizeFlags.ExpandFill;
            rows.AddChild(none);
        }
        else
        {
            var ranks = ResultRanks.Compute(_results);
            for (var i = 0; i < _results.Scores.Count && i < 8; i++)
            {
                var s = _results.Scores[i];
                var join = _joinOrder.ToList().IndexOf(s.PlayerId);
                rows.AddChild(Row(ranks[i], s, join >= 0 ? join : i));
            }
            // Keep rows a sensible height with few players.
            for (var i = _results.Scores.Count; i < 6; i++)
            {
                rows.AddChild(ArcTheme.Spacer(false, true));
            }
        }

        var table = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, headPanel, rows));
        table.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(table);

        _countdown = ArcTheme.Label("", ArcTheme.Bold, 24, ArcTheme.Dim);
        var (track, fill) = ShellLayout.Bar(4);
        _bar = fill;
        track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        track.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        column.AddChild(ArcTheme.HBox(24, _countdown, track));
    }

    public override void _Process(double delta)
    {
        var left = Math.Max(0, HoldSeconds - Elapsed);
        _countdown.Text = left > 0
            ? $"Back to the lobby in {Math.Ceiling(left):0} · press any key to skip"
            : "Back to the lobby when the room is ready · press any key";
        ShellLayout.SetBar(_bar, (float)(left / HoldSeconds));
        if (left <= 0)
        {
            Dismissed?.Invoke();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } or InputEventJoypadButton { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            Dismissed?.Invoke();
        }
    }

    private static Control Row(ResultRank rank, PlayerScore score, int colourIndex)
    {
        var lead = rank.Place == 1;
        var pos = ArcTheme.Label(rank.Place.ToString(), ArcTheme.Black, 44, lead ? new Color("111111") : Colors.White);
        pos.HorizontalAlignment = HorizontalAlignment.Center;
        pos.VerticalAlignment = VerticalAlignment.Center;
        var posCell = ArcTheme.Panel(ArcTheme.Flat(lead ? Colors.White : new Color(1, 1, 1, 0.04f)), pos);
        posCell.CustomMinimumSize = new Vector2(96, 0);

        var colour = new ColorRect { Color = ArcTheme.PlayerColour(colourIndex), CustomMinimumSize = new Vector2(7, 54), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var name = ArcTheme.Label(score.Name, ArcTheme.Bold, 46);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;

        var content = ArcTheme.HBox(22, posCell, colour, name);
        if (rank.Winner)
        {
            var chip = ArcTheme.Label("WINNER", ArcTheme.Spaced("Black", 2), 18, new Color("15151e"));
            var chipPanel = ArcTheme.Panel(ArcTheme.Flat(ArcTheme.Gold, 0), ArcTheme.Margin(14, 3, 12, 3, chip));
            chipPanel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            content.AddChild(chipPanel);
        }
        var points = ArcTheme.Label(score.Score.ToString(), ArcTheme.Black, 60);
        points.HorizontalAlignment = HorizontalAlignment.Right;
        points.VerticalAlignment = VerticalAlignment.Center;
        points.CustomMinimumSize = new Vector2(180, 0);
        content.AddChild(points);

        var row = ArcTheme.Panel(LobbyScreen.BottomEdge(lead ? new Color(1, 1, 1, 0.05f) : Colors.Transparent, 0, 0), ArcTheme.Margin(0, 0, 32, 0, content));
        row.SizeFlagsVertical = SizeFlags.ExpandFill;
        return row;
    }
}
