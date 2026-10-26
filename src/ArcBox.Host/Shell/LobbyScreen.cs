using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// Room code, QR, players (VIP and connection state), game picker and Start. Keyboard: ←/→ picks a game,
/// Enter starts. The server decides everything; this only shows the latest <see cref="LobbyState"/>.
/// </summary>
public partial class LobbyScreen : Control
{
    private const int Slots = 8;

    private readonly List<PlayerRow> _rows = [];
    private readonly Dictionary<string, (PanelContainer Card, Label Name, Label Players, ColorRect Bar)> _cards = new();
    private string _serverUrl = HostSettings.DefaultServer;
    private LobbyState? _lobby;
    private string? _qrFor;

    private Label _title = null!;
    private Label _code = null!;
    private TextureRect _qr = null!;
    private Label _joinUrl = null!;
    private Label _playerCount = null!;
    private HBoxContainer _gameRow = null!;
    private PanelContainer _start = null!;
    private Label _startText = null!;
    private Label _startHint = null!;
    private Label _toast = null!;
    private double _toastUntil;

    public event Action<string>? GameSelected;
    public event Action? StartPressed;

    public LobbyState? Lobby => _lobby;

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);

        _title = ArcTheme.Label("Grab your phone and join the room", ArcTheme.Bold, 40);
        column.AddChild(ShellLayout.Header("ARCBOX", ArcTheme.Meta("Lobby · join on your phone"), _title));

        var body = ArcTheme.HBox(24, BuildRoomPanel(), BuildPlayersPanel());
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(body);
        column.AddChild(BuildFooter());

        if (_lobby is not null)
        {
            Apply(_lobby);
        }
    }

    /// <summary>The URL phones use; the QR encodes <c>{serverUrl}/?room={CODE}</c>.</summary>
    public void SetServer(string serverUrl) => _serverUrl = serverUrl.TrimEnd('/');

    public static string JoinUrl(string serverUrl, string code) => $"{serverUrl.TrimEnd('/')}/?room={code}";

    public void ShowLobby(LobbyState lobby)
    {
        _lobby = lobby;
        if (IsNodeReady())
        {
            Apply(lobby);
        }
    }

    /// <summary>A short message under Start, e.g. the server's "Pick a game first."</summary>
    public void Toast(string message)
    {
        _toast.Text = message;
        _toastUntil = Time.GetTicksMsec() / 1000.0 + 4;
    }

    public override void _Process(double delta)
    {
        if (_toast.Text.Length > 0 && Time.GetTicksMsec() / 1000.0 > _toastUntil)
        {
            _toast.Text = "";
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_lobby is null || _lobby.InGame)
        {
            return;
        }

        if (e.IsActionPressed("ui_accept"))
        {
            StartPressed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_right") || e.IsActionPressed("ui_left"))
        {
            var games = _lobby.Games;
            if (games.Count > 0)
            {
                var step = e.IsActionPressed("ui_right") ? 1 : -1;
                var i = games.ToList().FindIndex(g => g.Id == _lobby.SelectedGameId);
                var next = i < 0 ? 0 : (i + step + games.Count) % games.Count;
                GameSelected?.Invoke(games[next].Id);
            }
            GetViewport().SetInputAsHandled();
        }
    }

    private Control BuildRoomPanel()
    {
        _code = ArcTheme.Label("····", ArcTheme.Display("Black", 34, 92, 78), 240);
        _code.HorizontalAlignment = HorizontalAlignment.Center;

        _qr = new TextureRect
        {
            CustomMinimumSize = new Vector2(270, 270),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        var qrCard = ArcTheme.Panel(ArcTheme.Flat(Colors.White, 10), _qr);
        qrCard.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        _joinUrl = ArcTheme.Label("", ArcTheme.Bold, 30);
        _joinUrl.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        _joinUrl.CustomMinimumSize = new Vector2(300, 0);

        var steps = ArcTheme.VBox(14,
            ArcTheme.Meta("Scan to join", 18, ArcTheme.Red),
            ArcTheme.Label("Point your phone camera at the code, or open", ArcTheme.SemiBold, 24, ArcTheme.Dim),
            _joinUrl,
            ArcTheme.Label("No app needed. Pick a name and you're in.", ArcTheme.SemiBold, 24, ArcTheme.Dim));
        steps.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        steps.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (var child in steps.GetChildren().OfType<Label>())
        {
            child.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        var edge = new ColorRect { Color = ArcTheme.Edge, CustomMinimumSize = new Vector2(0, 1) };
        var codeBlock = ArcTheme.VBox(0, ArcTheme.Meta("Room code"), _code);
        codeBlock.SizeFlagsVertical = SizeFlags.ExpandFill;
        codeBlock.Alignment = BoxContainer.AlignmentMode.Center;

        var content = ArcTheme.VBox(18, codeBlock, edge, ArcTheme.HBox(36, qrCard, steps));
        var panel = ArcTheme.Panel(ArcTheme.GlassBox(null, 40, 24), content);
        panel.CustomMinimumSize = new Vector2(1000, 0);
        return panel;
    }

    private Control BuildPlayersPanel()
    {
        _playerCount = ArcTheme.Meta("0 / 8");
        var head = ArcTheme.HBox(0, ArcTheme.Meta("Players"), ArcTheme.Spacer(), _playerCount);
        var headPanel = ArcTheme.Panel(BottomEdge(new Color(0, 0, 0, 0), 24, 12), head);

        var rows = ArcTheme.VBox(0);
        rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        for (var i = 0; i < Slots; i++)
        {
            var row = new PlayerRow(i);
            _rows.Add(row);
            rows.AddChild(row.Root);
        }

        var panel = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, headPanel, rows));
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return panel;
    }

    private Control BuildFooter()
    {
        _gameRow = ArcTheme.HBox(16);
        _gameRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _startText = ArcTheme.Label("START", ArcTheme.BlackItalic, 54);
        _startText.HorizontalAlignment = HorizontalAlignment.Center;
        _startHint = ArcTheme.Meta("Press Enter", 16, Colors.White);
        _startHint.HorizontalAlignment = HorizontalAlignment.Center;
        var startBox = ArcTheme.VBox(0, _startText, _startHint);
        startBox.Alignment = BoxContainer.AlignmentMode.Center;
        _start = ArcTheme.Panel(ArcTheme.Flat(ArcTheme.Red, 0), startBox);
        _start.CustomMinimumSize = new Vector2(400, 128);
        _start.MouseFilter = MouseFilterEnum.Stop;
        _start.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                StartPressed?.Invoke();
            }
        };

        _toast = ArcTheme.Label("", ArcTheme.Bold, 22, ArcTheme.Bad);
        var pickHead = ArcTheme.HBox(18,
            ArcTheme.Meta("Choose a game"),
            ArcTheme.Label("on the VIP's phone, or ← → here", ArcTheme.SemiBold, 20, ArcTheme.Dim),
            ArcTheme.Spacer(),
            _toast);
        var picker = ArcTheme.VBox(12, pickHead, _gameRow);
        picker.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var footer = ArcTheme.HBox(24, picker, _start);
        footer.CustomMinimumSize = new Vector2(0, 128);
        return footer;
    }

    private void Apply(LobbyState lobby)
    {
        _code.Text = lobby.Code;
        var url = JoinUrl(_serverUrl, lobby.Code);
        _joinUrl.Text = url;
        if (_qrFor != url)
        {
            _qr.Texture = QrTexture.Create(url);
            _qrFor = url;
        }

        var selected = lobby.Games.FirstOrDefault(g => g.Id == lobby.SelectedGameId);
        var max = Math.Min(Slots, selected?.MaxPlayers ?? Slots);
        _playerCount.Text = $"{lobby.Players.Count} / {max}";
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Show(i < lobby.Players.Count ? lobby.Players[i] : null, i < max);
        }

        var vip = lobby.Players.FirstOrDefault(p => p.IsVip);
        _title.Text = lobby.Players.Count == 0
            ? "Grab your phone and join the room"
            : vip is not null ? $"{vip.Name} is the VIP: pick a game, then start" : "Pick a game, then start";

        ApplyGames(lobby, selected);
        ApplyStart(lobby, selected);
    }

    private void ApplyGames(LobbyState lobby, GameManifest? selected)
    {
        var ids = lobby.Games.Select(g => g.Id).ToList();
        if (!ids.SequenceEqual(_cards.Keys))
        {
            foreach (var child in _gameRow.GetChildren())
            {
                child.QueueFree();
            }
            _cards.Clear();
            foreach (var game in lobby.Games)
            {
                _cards[game.Id] = BuildCard(game);
            }
        }

        foreach (var game in lobby.Games)
        {
            var (card, name, players, bar) = _cards[game.Id];
            var on = game.Id == selected?.Id;
            card.AddThemeStyleboxOverride("panel", on
                ? ArcTheme.Flat(new Color(1, 1, 1, 0.10f), 0, new Color(1, 1, 1, 0.35f), 1)
                : ArcTheme.GlassBox(null, 0, 0));
            bar.Color = on ? ArcTheme.Red : new Color(1, 1, 1, 0.08f);
            name.AddThemeColorOverride("font_color", on ? Colors.White : new Color(1, 1, 1, 0.75f));
            var fits = lobby.Players.Count >= game.MinPlayers && lobby.Players.Count <= game.MaxPlayers;
            players.Text = $"{PlayersText(game)}{(on ? " · SELECTED" : "")}";
            players.AddThemeColorOverride("font_color", on ? (fits ? ArcTheme.Good : ArcTheme.Gold) : ArcTheme.Dim);
        }
    }

    private (PanelContainer, Label, Label, ColorRect) BuildCard(GameManifest game)
    {
        var bar = new ColorRect { CustomMinimumSize = new Vector2(6, 0), MouseFilter = MouseFilterEnum.Ignore };
        var name = ArcTheme.Label(game.Name.ToUpperInvariant(), ArcTheme.BlackItalic, 36);
        var desc = ArcTheme.Label(game.Description, ArcTheme.SemiBold, 19, ArcTheme.Dim);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.MaxLinesVisible = 2;
        desc.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        desc.CustomMinimumSize = new Vector2(240, 0);
        var players = ArcTheme.Meta(PlayersText(game), 15);
        var text = ArcTheme.VBox(2, name, desc, ArcTheme.Spacer(false, true), players);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var inner = ArcTheme.Margin(0, 12, 18, 12, text);
        inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var card = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.HBox(18, bar, inner));
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsVertical = SizeFlags.ExpandFill;
        card.MouseFilter = MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = CursorShape.PointingHand;
        foreach (var c in new Control[] { bar, name, desc, players, text, inner })
        {
            c.MouseFilter = MouseFilterEnum.Ignore;
        }
        var id = game.Id;
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                GameSelected?.Invoke(id);
            }
        };
        _gameRow.AddChild(card);
        return (card, name, players, bar);
    }

    private static string PlayersText(GameManifest g) =>
        g.MinPlayers == g.MaxPlayers ? $"{g.MinPlayers} PLAYERS" : $"{g.MinPlayers}–{g.MaxPlayers} PLAYERS";

    private void ApplyStart(LobbyState lobby, GameManifest? selected)
    {
        string hint;
        if (selected is null)
        {
            hint = "Pick a game first";
        }
        else if (lobby.Players.Count < selected.MinPlayers)
        {
            var need = selected.MinPlayers - lobby.Players.Count;
            hint = $"Waiting for {need} more player{(need == 1 ? "" : "s")}";
        }
        else if (lobby.Players.Count > selected.MaxPlayers)
        {
            hint = $"Too many players for {selected.Name}";
        }
        else
        {
            hint = lobby.CanStart ? "Everybody's in? Press Enter" : "Not ready yet";
        }

        _startHint.Text = hint.ToUpperInvariant();
        _start.AddThemeStyleboxOverride("panel", lobby.CanStart
            ? ArcTheme.Flat(ArcTheme.Red, 0)
            : ArcTheme.Flat(new Color(1, 1, 1, 0.05f), 0, new Color(1, 1, 1, 0.12f), 1));
        _startText.AddThemeColorOverride("font_color", lobby.CanStart ? Colors.White : new Color(1, 1, 1, 0.3f));
        _startHint.AddThemeColorOverride("font_color", lobby.CanStart ? Colors.White : ArcTheme.Dim);
    }

    internal static StyleBoxFlat BottomEdge(Color bg, int padH, int padV)
    {
        var sb = ArcTheme.Flat(bg, 0);
        sb.ContentMarginLeft = sb.ContentMarginRight = padH;
        sb.ContentMarginTop = sb.ContentMarginBottom = padV;
        sb.BorderColor = ArcTheme.Edge;
        sb.BorderWidthBottom = 1;
        return sb;
    }

    /// <summary>One slot in the players list (the timing-tower row look).</summary>
    private sealed class PlayerRow
    {
        private readonly int _index;
        private readonly Label _pos;
        private readonly PanelContainer _posCell;
        private readonly ColorRect _colour;
        private readonly Label _name;
        private readonly PanelContainer _vip;
        private readonly ColorRect _dot;
        private readonly Label _state;

        public PlayerRow(int index)
        {
            _index = index;
            _pos = ArcTheme.Label((index + 1).ToString(), ArcTheme.Black, 34);
            _pos.HorizontalAlignment = HorizontalAlignment.Center;
            _pos.VerticalAlignment = VerticalAlignment.Center;
            _posCell = ArcTheme.Panel(ArcTheme.Flat(new Color(1, 1, 1, 0.04f)), _pos);
            _posCell.CustomMinimumSize = new Vector2(78, 0);

            _colour = new ColorRect { CustomMinimumSize = new Vector2(6, 46), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            _name = ArcTheme.Label("", ArcTheme.Bold, 40);
            _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _name.ClipText = true;

            var vipLabel = ArcTheme.Label("VIP", ArcTheme.Spaced("Black", 2), 17, new Color("15151e"));
            _vip = ArcTheme.Panel(ArcTheme.Flat(ArcTheme.Gold, 0), ArcTheme.Margin(12, 2, 10, 2, vipLabel));
            _vip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

            _dot = new ColorRect { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            _state = ArcTheme.Meta("", 16);
            _state.CustomMinimumSize = new Vector2(110, 0);

            var content = ArcTheme.HBox(18, _posCell, _colour, _name, _vip, ArcTheme.HBox(10, _dot, _state));
            content.GetChild<Control>(4).SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            Root = ArcTheme.Panel(BottomEdge(Colors.Transparent, 0, 0), ArcTheme.Margin(0, 0, 24, 0, content));
            Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        }

        public PanelContainer Root { get; }

        public void Show(LobbyPlayer? player, bool slotOpen)
        {
            Root.Visible = slotOpen || player is not null;
            if (player is null)
            {
                _pos.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.18f));
                _colour.Color = new Color(1, 1, 1, 0.08f);
                _name.Text = "Waiting for a player…";
                ArcTheme.Style(_name, ArcTheme.SemiBold, 28, new Color(1, 1, 1, 0.22f));
                _vip.Visible = false;
                _dot.Visible = false;
                _state.Text = "";
                return;
            }

            _pos.AddThemeColorOverride("font_color", Colors.White);
            _colour.Color = ArcTheme.PlayerColour(_index);
            _name.Text = player.Name;
            ArcTheme.Style(_name, ArcTheme.Bold, 40, player.Connected ? Colors.White : new Color(1, 1, 1, 0.45f));
            _vip.Visible = player.IsVip;
            _dot.Visible = true;
            _dot.Color = player.Connected ? ArcTheme.Good : ArcTheme.Dim;
            _state.Text = player.Connected ? "READY" : "AWAY";
            _state.AddThemeColorOverride("font_color", player.Connected ? ArcTheme.Good : ArcTheme.Dim);
        }
    }
}
