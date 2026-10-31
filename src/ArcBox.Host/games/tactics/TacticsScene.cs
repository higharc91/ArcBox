using System.Diagnostics;
using System.Text.Json;
using ArcBox.Host.Audio;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// ArcBox Tactics on the TV: the banner, and for each phase its table from <see cref="PhaseScreen"/> (the Muster roster,
/// the encounter list, the results), and in battle the board and its units (<see cref="BoardCanvas"/>). Everything worded or worked out
/// here comes from <c>Tactics.Presentation</c>; this class only draws it.
/// <para>
/// In battle, what's drawn is <see cref="BeatPlayer.Shown"/>, never the view: the view is already past the beat on screen.
/// Each beat that finishes sends <c>shown</c>. That only ever shortens the game's wait, so nothing here can hold the game up.
/// </para>
/// </summary>
public partial class TacticsScene : Control, IGameScene, IContentScene, ISignalingScene, IAudioScene
{
    /// <summary>A table this short leaves its rows at this share of the height, not stretched into slabs.</summary>
    private const int RowSlots = 6;

    private const string BasePack = "base";

    /// <summary>How many screenshots <c>--shots</c> takes of each kind of moment.</summary>
    private const int ShotsPerMoment = 6;

    private Label _meta = null!;
    private Label _title = null!;
    private Label _level = null!;
    private PanelContainer _notice = null!;
    private Label _noticeText = null!;
    private VBoxContainer _rows = null!;
    private Label _empty = null!;
    private Control _header = null!;
    private Control _table = null!;
    private BoardCanvas _board = null!;
    private Control _battleHead = null!;
    private Label _battleMeta = null!;
    private Label _battleTitle = null!;
    private ArtIndex? _art;
    private UnitArt? _unitArt;
    private BattleMap? _map;
    private BoardFit? _fit;
    private string? _unreadableMap;
    private Screenshotter? _shots;
    private IReadOnlyDictionary<string, string> _content = new Dictionary<string, string>();
    private Action<string, object> _signal = (_, _) => { };
    private string? _lastShot;
    private FxArt? _fx;
    private BeatPlayer? _player;
    private BoardTerrain? _terrain;
    private BoardChanges? _planned;
    private PhaseScreen? _held;
    private Narrator? _narrator;
    private PanelContainer _caption = null!;
    private Label _captionText = null!;
    private BeatScript? _playing;
    private double _playingSince;
    private readonly Dictionary<string, int> _moments = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary><c>--beat-log</c>: print a <c>BEAT</c> line with each beat's timing.</summary>
    public bool LogBeats { get; set; }

    public void SetNarrator(Narrator narrator) => _narrator = narrator;

    public void SetShots(Screenshotter? shots) => _shots = shots;

    public void SetContentRoots(IReadOnlyDictionary<string, string> rootsByPackId)
    {
        _content = rootsByPackId;
        GD.Print($"Tactics content: {string.Join(", ", _content.Select(p => $"{p.Key} at {p.Value}"))}");
    }

    public Action<string, object> Signal { set => _signal = value; }

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);

        _meta = ArcTheme.Meta("Tactics");
        _title = ArcTheme.Label("", ArcTheme.Bold, 42);
        _level = ArcTheme.Label("1", ArcTheme.Display("Black", 0, 18, 14), 60);
        _header = ShellLayout.Header("TACTICS", _meta, _title, ShellLayout.StatBox(ArcTheme.Meta("Party level", 16), _level, null, 220));
        column.AddChild(_header);

        _board = new BoardCanvas { Visible = false };
        AddChild(_board);
        MoveChild(_board, 1);   // over the background, under the column

        _battleMeta = ArcTheme.Meta("Battle", 16);
        _battleTitle = ArcTheme.Label("", ArcTheme.Bold, 30);
        _battleHead = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Red, 20, 8), ArcTheme.VBox(0, _battleMeta, _battleTitle));
        _battleHead.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _battleHead.Visible = false;
        column.AddChild(_battleHead);

        _noticeText = ArcTheme.Label("", ArcTheme.SemiBold, 28, ArcTheme.Gold);
        _notice = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 28, 10), _noticeText);
        _notice.Visible = false;
        column.AddChild(_notice);

        _rows = ArcTheme.VBox(0);
        _rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        _empty = ArcTheme.Label("", ArcTheme.Black, 72);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.SizeFlagsVertical = SizeFlags.ExpandFill;
        _empty.Visible = false;

        _table = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, _rows, _empty));
        _table.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(_table);

        _captionText = ArcTheme.Label("", ArcTheme.SemiBold, 34);
        _caption = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 28, 12), _captionText);
        _caption.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _caption.GrowHorizontal = GrowDirection.Both;
        _caption.GrowVertical = GrowDirection.Begin;
        _caption.OffsetBottom = -40;
        _caption.Visible = false;
        AddChild(_caption);
    }

    public override void _Process(double delta)
    {
        if (_player is null || !_board.Visible)
        {
            return;
        }

        var tick = _player.Tick(delta);
        if (tick.Started is { } started)
        {
            (_playing, _playingSince) = (started, _clock.Elapsed.TotalSeconds);
        }
        foreach (var seq in tick.Shown)
        {
            LogBeat(seq);
            _signal("shown", new { seq });
        }
        foreach (var line in tick.Lines)
        {
            _narrator?.Say(line.Text, line.Theme, NarrationPriority.Commentary);
        }
        foreach (var moment in tick.Moments)
        {
            Shoot(moment);
        }
        ShowCaption(_player.Caption);
        if (tick.BoardChanged || tick.Busy)
        {
            Redraw();
        }
        if (_held is { } held && _player.Idle)
        {
            _held = null;
            Show(held);
        }
    }

    private void LogBeat(int seq)
    {
        if (LogBeats && _playing is { } beat)
        {
            var now = _clock.Elapsed.TotalSeconds;
            var kinds = string.Join(',', beat.Cues.OfType<ApplyCue>().Select(c => c.Event.GetType().Name).Distinct());
            var swings = beat.Cues.Count(c => c is LungeCue or FlightCue);
            GD.Print(FormattableString.Invariant($"BEAT {seq} from {_playingSince:0.000} to {now:0.000} took {now - _playingSince:0.000} scripted {beat.Length:0.000} swings {swings} waiting {_player?.Waiting} [{kinds}]"));
        }
    }

    private void Shoot(string moment)
    {
        var taken = _moments.GetValueOrDefault(moment);
        if (_shots is not null && taken < ShotsPerMoment)
        {
            _moments[moment] = taken + 1;
            _shots.Capture($"tactics-beat-{moment}-{_playing?.Seq}");
        }
    }

    private void ShowCaption(SpokenLine? line)
    {
        _caption.Visible = line is not null;
        if (line is not null)
        {
            _captionText.Text = line.Speaker is { } speaker ? $"{speaker}: {line.Text}" : line.Text;
        }
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        if (view.Deserialize<TacticsHostView>(ArcJson.Options) is not { } v)
        {
            return;
        }

        var screen = PhaseScreen.Of(v);

        // The battle ended with beats still to play: they finish first, and then this screen. They were the game's last, so nothing waits on them.
        if (screen.Board is null && v.LastBattle is { } last && _player?.Shown is not null)
        {
            _player.Receive(last);
            if (!_player.Idle)
            {
                _held = screen;
                return;
            }
        }
        _held = null;
        Show(screen);
    }

    private void Show(PhaseScreen screen)
    {
        _meta.Text = screen.Meta.ToUpperInvariant();
        _title.Text = screen.Title;
        _title.AddThemeColorOverride("font_color", Accent(screen.Tone, Colors.White));
        _level.Text = screen.PartyLevel.ToString();
        _notice.Visible = screen.Notice is not null;
        _noticeText.Text = screen.Notice ?? "";
        _empty.Visible = screen.Empty is not null;
        _empty.Text = screen.Empty ?? "";
        _rows.Visible = screen.Empty is null;
        ShowRows(screen);
        ShowBoard(screen);

        if (screen.Shot != _lastShot)
        {
            _lastShot = screen.Shot;
            _shots?.Capture(screen.Shot);
        }
    }

    /// <summary>The board replaces the header and table in battle. If it can't be drawn, they stay, with the encounter's name.</summary>
    private void ShowBoard(PhaseScreen screen)
    {
        var drawn = screen.Board is { } battle && DrawBoard(battle);
        if (!drawn)
        {
            _player?.Reset();
            _caption.Visible = false;
        }
        _board.Visible = drawn;
        _battleHead.Visible = drawn;
        _header.Visible = !drawn;
        _table.Visible = !drawn;
        _battleMeta.Text = screen.Meta.ToUpperInvariant();
        _battleTitle.Text = screen.Title;
    }

    private bool DrawBoard(BattleView battle)
    {
        if (!_content.TryGetValue(BasePack, out var root))
        {
            return false;
        }
        try
        {
            _art ??= ArtIndex.Load(root);
            _unitArt ??= UnitArt.Load(root);
            _fx ??= FxArt.Load(root);
            _player ??= new BeatPlayer(_fx, _unitArt);
            if (_map?.Id != battle.Map)
            {
                _map = BoardMap.Parse(File.ReadAllText(Path.Combine(root, "rules", "maps", $"{battle.Map}.json")));
                _fit = BoardFit.For(new BoardTerrain(_map, null), _art);   // once per map: a fight never moves the board
                _terrain = null;
                _player.Reset();
            }
            _player.Receive(battle);
            Redraw();
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentException
            or InvalidOperationException or KeyNotFoundException)
        {
            if (_unreadableMap != battle.Map)
            {
                _unreadableMap = battle.Map;
                GD.PushError($"Tactics board: can't draw map '{battle.Map}': {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>Draws the battle as the player has seen it. The board's plan is only rebuilt when the board has changed.</summary>
    private void Redraw()
    {
        if (_player?.Shown is not { } shown || _map is null || _fit is not { } fit || _art is null || _unitArt is null || !_content.TryGetValue(BasePack, out var root))
        {
            return;
        }
        if (_terrain is null || !DisplayedBattle.SameBoard(_planned, shown.Board))
        {
            _terrain = new BoardTerrain(_map, shown.Board);
            _planned = shown.Board;
            _board.SetPlan(BoardPlan.Build(_terrain, _art), fit, root);
        }
        var frame = _player.Frame(_terrain);
        _board.SetUnits(UnitPlan.Build(shown, _terrain, _unitArt, fit, frame.Places, frame.Poses), _unitArt);
        _board.SetEffects(frame.Draws, frame.Shake);
    }

    private void ShowRows(PhaseScreen screen)
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in screen.Rows)
        {
            _rows.AddChild(Row(row, Accent(screen.Tone, ArcTheme.Gold)));
        }
        if (!screen.Rows.IsEmpty && screen.Rows.Length < RowSlots)
        {
            var filler = ArcTheme.Spacer(false, true);
            filler.SizeFlagsStretchRatio = RowSlots - screen.Rows.Length;
            _rows.AddChild(filler);
        }
    }

    private static Color Accent(Tone tone, Color neutral) => tone switch
    {
        Tone.Good => ArcTheme.Good,
        Tone.Bad => ArcTheme.Bad,
        _ => neutral,
    };

    private static Control Row(ScreenRow row, Color accent)
    {
        var bar = new ColorRect { Color = accent, CustomMinimumSize = new Vector2(7, 54), SizeFlagsVertical = SizeFlags.ShrinkCenter };

        var name = ArcTheme.Label(row.Title, ArcTheme.Bold, 46);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        var text = ArcTheme.VBox(0, name);
        text.Alignment = BoxContainer.AlignmentMode.Center;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (row.Detail.Length > 0)
        {
            text.AddChild(ArcTheme.Label(row.Detail, ArcTheme.SemiBold, 26, ArcTheme.Dim));
        }

        var content = ArcTheme.HBox(22, bar, text);
        if (row.Chip is { } chip)
        {
            var label = ArcTheme.Label(chip, ArcTheme.Spaced("Black", 2), 18, new Color("15151e"));
            var panel = ArcTheme.Panel(ArcTheme.Flat(accent), ArcTheme.Margin(14, 3, 12, 3, label));
            panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            content.AddChild(panel);
        }
        if (row.Value is { } value)
        {
            var label = ArcTheme.Label(value, ArcTheme.Black, 56);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.CustomMinimumSize = new Vector2(220, 0);
            content.AddChild(label);
        }

        var root = ArcTheme.Panel(LobbyScreen.BottomEdge(Colors.Transparent, 0, 0), ArcTheme.Margin(32, 0, 32, 0, content));
        root.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.Modulate = row.Dim ? new Color(1, 1, 1, 0.4f) : Colors.White;
        return root;
    }
}
