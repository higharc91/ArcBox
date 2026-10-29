using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Counter.Logic;
using Godot;

namespace ArcBox.Host.Games.Counter;

/// <summary>Counter's TV view: one row per player with a bar and a big press count, sorted by count, plus the clock.</summary>
public partial class CounterScene : Control, IGameScene
{
    private const double FinalSeconds = 3;

    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private VBoxContainer _list = null!;
    private Label _clock = null!;
    private ColorRect _clockBar = null!;
    private Label _title = null!;
    private CounterHostView? _view;
    private Screenshotter? _shots;
    private bool _finalShot;

    public void SetShots(Screenshotter? shots) => _shots = shots;

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);

        _clock = ArcTheme.Label("0:20", ArcTheme.Display("Black", 0, 18, 14), 60);
        var (track, fill) = ShellLayout.Bar(5);
        _clockBar = fill;
        _title = ArcTheme.Label("Tap +1 on your phone as fast as you can!", ArcTheme.Bold, 42);
        column.AddChild(ShellLayout.Header(
            "COUNTER",
            ArcTheme.Meta("Mash the button · most presses wins"),
            _title,
            ShellLayout.StatBox(ArcTheme.Meta("Time", 16), _clock, track, 220)));

        _list = ArcTheme.VBox(0);
        _list.SizeFlagsVertical = SizeFlags.ExpandFill;
        var head = ArcTheme.HBox(0, ArcTheme.Meta("Pos · Player"), ArcTheme.Spacer(), ArcTheme.Meta("Presses"));
        var headPanel = ArcTheme.Panel(LobbyScreen.BottomEdge(Colors.Transparent, 28, 12), head);
        var table = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, headPanel, _list));
        table.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(table);
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        var v = view.Deserialize<CounterHostView>(ArcJson.Options);
        if (v is null)
        {
            return;
        }
        _view = v;

        var max = Math.Max(1, v.Rows.Count == 0 ? 1 : v.Rows.Max(r => r.Count));
        var order = v.Rows
            .Select((r, joinIndex) => (r, joinIndex))
            .OrderByDescending(x => x.r.Count)
            .ThenBy(x => x.joinIndex)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rank = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var (r, joinIndex) = order[i];
            seen.Add(r.Name);
            if (!_rows.TryGetValue(r.Name, out var row))
            {
                row = new Row(r.Name, ArcTheme.PlayerColour(joinIndex));
                _rows[r.Name] = row;
                _list.AddChild(row.Root);
            }
            _list.MoveChild(row.Root, i);
            if (i == 0 || r.Count != order[i - 1].r.Count)
            {
                rank = i + 1;   // ties share a position
            }
            row.Set(rank, r.Count, (float)r.Count / max, rank == 1 && r.Count > 0);
        }

        foreach (var gone in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _rows[gone].Root.QueueFree();
            _rows.Remove(gone);
        }

        // Few players: keep rows from stretching into giant slabs.
        var filler = _list.GetChildren().OfType<Control>().FirstOrDefault(c => c.Name == "Filler");
        if (filler is null)
        {
            filler = ArcTheme.Spacer(false, true);
            filler.Name = "Filler";
            _list.AddChild(filler);
        }
        filler.SizeFlagsStretchRatio = Math.Max(0, 6 - order.Count);
        filler.Visible = order.Count < 6;
        _list.MoveChild(filler, -1);
    }

    public override void _Process(double delta)
    {
        foreach (var row in _rows.Values)
        {
            row.Tick((float)delta);
        }

        if (_view?.EndsAt is { } endsAt)
        {
            var left = Math.Clamp((endsAt - DateTimeOffset.UtcNow).TotalSeconds, 0, CounterGame.Duration.TotalSeconds);
            var secs = (int)Math.Ceiling(left);
            _clock.Text = $"{secs / 60}:{secs % 60:00}";
            ShellLayout.SetBar(_clockBar, (float)(left / CounterGame.Duration.TotalSeconds));
            _clock.AddThemeColorOverride("font_color", left <= FinalSeconds ? ArcTheme.Red : Colors.White);
            if (left <= FinalSeconds && !_finalShot && _rows.Count > 0)
            {
                _finalShot = true;
                _title.Text = "Final seconds: keep mashing!";
                _shots?.Capture("counter-final");
            }
        }
    }

    private sealed class Row
    {
        private readonly Label _pos;
        private readonly PanelContainer _posCell;
        private readonly ColorRect _fill;
        private readonly Label _count;
        private float _shownFraction;
        private float _targetFraction;
        private float _shownCount;
        private int _targetCount;
        private float _flash;

        public Row(string name, Color colour)
        {
            _pos = ArcTheme.Label("1", ArcTheme.Black, 40);
            _pos.HorizontalAlignment = HorizontalAlignment.Center;
            _pos.VerticalAlignment = VerticalAlignment.Center;
            _posCell = ArcTheme.Panel(ArcTheme.Flat(new Color(1, 1, 1, 0.04f)), _pos);
            _posCell.CustomMinimumSize = new Vector2(96, 0);

            var bar = new ColorRect { Color = colour, CustomMinimumSize = new Vector2(7, 54), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            var nameLabel = ArcTheme.Label(name, ArcTheme.Bold, 44);
            nameLabel.CustomMinimumSize = new Vector2(340, 0);
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            nameLabel.ClipText = true;

            var track = new ColorRect { Color = new Color(1, 1, 1, 0.07f), CustomMinimumSize = new Vector2(0, 22) };
            track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            track.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _fill = new ColorRect { Color = colour };
            _fill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            ShellLayout.SetBar(_fill, 0);
            track.AddChild(_fill);

            _count = ArcTheme.Label("0", ArcTheme.Black, 84);
            _count.HorizontalAlignment = HorizontalAlignment.Right;
            _count.VerticalAlignment = VerticalAlignment.Center;
            _count.CustomMinimumSize = new Vector2(200, 0);

            var content = ArcTheme.HBox(24, _posCell, bar, nameLabel, track, _count);
            Root = ArcTheme.Panel(LobbyScreen.BottomEdge(Colors.Transparent, 0, 0), ArcTheme.Margin(0, 0, 36, 0, content));
            Root.SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        public PanelContainer Root { get; }

        public void Set(int rank, int count, float fraction, bool leader)
        {
            _pos.Text = rank.ToString();
            _posCell.AddThemeStyleboxOverride("panel", ArcTheme.Flat(leader ? Colors.White : new Color(1, 1, 1, 0.04f)));
            _pos.AddThemeColorOverride("font_color", leader ? new Color("111111") : Colors.White);
            if (count > _targetCount)
            {
                _flash = 1;
            }
            _targetCount = count;
            _targetFraction = fraction;
        }

        public void Tick(float delta)
        {
            var k = 1 - Mathf.Exp(-delta * 12);
            _shownFraction = Mathf.Lerp(_shownFraction, _targetFraction, k);
            _shownCount = Mathf.Lerp(_shownCount, _targetCount, k);
            if (Math.Abs(_shownCount - _targetCount) < 0.5f)
            {
                _shownCount = _targetCount;
            }
            ShellLayout.SetBar(_fill, _shownFraction);
            _count.Text = Mathf.RoundToInt(_shownCount).ToString();
            _flash = Math.Max(0, _flash - (delta * 4));
            _count.Modulate = Colors.White.Lerp(new Color(1, 0.85f, 0.2f), _flash);
        }
    }
}
