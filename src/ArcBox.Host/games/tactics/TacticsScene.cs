using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// ArcBox Tactics on the TV: the banner, and for each phase its table from <see cref="PhaseScreen"/> (the Muster roster,
/// the encounter list, the results), and in battle the board (<see cref="BoardCanvas"/>). Everything worded or worked out
/// here comes from <c>Tactics.Presentation</c>; this class only draws it.
/// </summary>
public partial class TacticsScene : Control, IGameScene, IContentScene, ISignalingScene
{
    /// <summary>A table this short leaves its rows at this share of the height, not stretched into slabs.</summary>
    private const int RowSlots = 6;

    private const string BasePack = "base";

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
    private BattleMap? _map;
    private BoardFit? _fit;
    private string? _unreadableMap;
    private Screenshotter? _shots;
    private IReadOnlyDictionary<string, string> _content = new Dictionary<string, string>();
    private Action<string, object> _signal = (_, _) => { };
    private string? _lastShot;

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
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        if (view.Deserialize<TacticsHostView>(ArcJson.Options) is not { } v)
        {
            return;
        }

        var screen = PhaseScreen.Of(v);
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
            if (_map?.Id != battle.Map)
            {
                _map = BoardMap.Parse(File.ReadAllText(Path.Combine(root, "rules", "maps", $"{battle.Map}.json")));
                _fit = BoardFit.For(new BoardTerrain(_map, null), _art);   // once per map: a fight never moves the board
            }
            var terrain = new BoardTerrain(_map, battle.Board);
            _board.SetPlan(BoardPlan.Build(terrain, _art), _fit ?? throw new InvalidOperationException("A map with no fit."), root);
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
