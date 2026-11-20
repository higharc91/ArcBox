using ArcBox.Host.Core;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>
/// Room code, QR, players (VIP and connection state), game picker and Start. Keyboard: ←/→ picks a game,
/// Enter starts. The server decides everything; this only shows the latest <see cref="LobbyState"/>.
/// For a game that uses campaigns, a third column lists its campaigns (click to select) and the selected campaign's
/// roster; for other games the lobby is unchanged.
/// </summary>
public partial class LobbyScreen : Control
{
    private const int Slots = 8;
    private const int RoomPanelWidth = 1000;
    private const int RoomPanelWidthWithCampaigns = 720;

    private readonly List<PlayerRow> _rows = [];
    private readonly Dictionary<string, (PanelContainer Card, Label Name, Label Players, ColorRect Bar)> _cards = new();
    private string _serverUrl = HostSettings.DefaultServer;
    private LobbyState? _lobby;
    private string? _qrFor;
    private readonly Dictionary<string, (PanelContainer Card, Label Name, ColorRect Bar)> _campaignCards = new();
    private string? _campaignListKey;
    private string? _rosterKey;

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

    private Control _roomPanel = null!;
    private PanelContainer _campaignPanel = null!;
    private Label _campaignCount = null!;
    private Control _campaignListSection = null!;
    private VBoxContainer _campaignList = null!;
    private Control _rosterSection = null!;
    private Label _rosterCode = null!;
    private Label _rosterName = null!;
    private VBoxContainer _roster = null!;

    public event Action<string>? GameSelected;

    /// <summary>A campaign in the list was clicked (its id).</summary>
    public event Action<string>? CampaignSelected;
    public event Action? StartPressed;

    public LobbyState? Lobby => _lobby;

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);

        _title = ArcTheme.Label("Grab your phone and join the room", ArcTheme.Bold, 40);
        column.AddChild(ShellLayout.Header("ARCBOX", ArcTheme.Meta("Lobby · join on your phone"), _title));

        var body = ArcTheme.HBox(24, BuildRoomPanel(), BuildPlayersPanel(), BuildCampaignPanel());
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
        panel.CustomMinimumSize = new Vector2(RoomPanelWidth, 0);
        _roomPanel = panel;
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

    /// <summary>The campaign column: the selected game's campaigns, then the selected campaign's roster. Hidden for other games.</summary>
    private Control BuildCampaignPanel()
    {
        _campaignCount = ArcTheme.Meta("");
        var head = ArcTheme.HBox(0, ArcTheme.Meta("Campaign"), ArcTheme.Spacer(), _campaignCount);
        var headPanel = ArcTheme.Panel(BottomEdge(new Color(0, 0, 0, 0), 24, 12), head);

        var hint = ArcTheme.Label("New campaign is created on the VIP's phone", ArcTheme.SemiBold, 18, ArcTheme.Dim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _campaignList = ArcTheme.VBox(8);
        _campaignList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_campaignList);
        var list = ArcTheme.VBox(12, hint, scroll);
        list.SizeFlagsVertical = SizeFlags.ExpandFill;
        _campaignListSection = ArcTheme.Margin(24, 14, 24, 14, list);
        _campaignListSection.SizeFlagsVertical = SizeFlags.ExpandFill;
        _campaignListSection.SizeFlagsStretchRatio = 1;

        _rosterCode = ArcTheme.Meta("", 15, ArcTheme.Red);
        _rosterName = ArcTheme.Label("", ArcTheme.BlackItalic, 30);
        _rosterName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _rosterName.ClipText = true;
        var rosterHead = ArcTheme.Panel(BottomEdge(new Color(1, 1, 1, 0.03f), 24, 10), ArcTheme.VBox(0, _rosterCode, _rosterName));
        _roster = ArcTheme.VBox(0);
        _roster.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var rosterScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        rosterScroll.AddChild(_roster);
        // With both shown, the roster gets most of the column (five members fit without scrolling, and three campaigns).
        _rosterSection = ArcTheme.VBox(0, rosterHead, rosterScroll);
        _rosterSection.SizeFlagsVertical = SizeFlags.ExpandFill;
        _rosterSection.SizeFlagsStretchRatio = 1.6f;

        _campaignPanel = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, headPanel, _campaignListSection, _rosterSection));
        _campaignPanel.CustomMinimumSize = new Vector2(440, 0);
        _campaignPanel.Visible = false;
        return _campaignPanel;
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
        ApplyCampaigns(lobby);
        ApplyStart(lobby, selected);
    }

    private void ApplyCampaigns(LobbyState lobby)
    {
        var shown = lobby.Campaigns is not null || lobby.Campaign is not null;
        _campaignPanel.Visible = shown;
        _roomPanel.CustomMinimumSize = new Vector2(shown ? RoomPanelWidthWithCampaigns : RoomPanelWidth, 0);
        if (!shown)
        {
            return;
        }

        _campaignListSection.Visible = lobby.Campaigns is not null;
        if (lobby.Campaigns is { } campaigns)
        {
            _campaignCount.Text = $"{campaigns.Count} SAVED";
            var key = string.Join("|", campaigns.Select(c => $"{c.Id}:{c.Name}:{c.Code}:{c.Members}"));
            if (key != _campaignListKey)
            {
                _campaignListKey = key;
                BuildCampaignList(campaigns);
            }
            foreach (var (id, (card, name, bar)) in _campaignCards)
            {
                var on = id == lobby.Campaign?.Id;
                card.AddThemeStyleboxOverride("panel", on
                    ? ArcTheme.Flat(new Color(1, 1, 1, 0.10f), 0, new Color(1, 1, 1, 0.35f), 1)
                    : ArcTheme.GlassBox(null, 0, 0));
                bar.Color = on ? ArcTheme.Red : new Color(1, 1, 1, 0.08f);
                name.AddThemeColorOverride("font_color", on ? Colors.White : new Color(1, 1, 1, 0.75f));
            }
        }
        else
        {
            _campaignCount.Text = "";
        }

        _rosterSection.Visible = lobby.Campaign is not null;
        if (lobby.Campaign is { } campaign)
        {
            _rosterCode.Text = $"{campaign.Code} · {campaign.Roster.Count(r => r.SeatedAs is not null && r.Connected)} OF {campaign.Roster.Count} HERE";
            _rosterName.Text = campaign.Name;
            var key = string.Join("|", campaign.Roster.Select(r =>
                $"{r.Profile}:{r.Card}:{r.SeatedAs}:{r.Connected}:{PlayerIndex(lobby, r.SeatedAs)}"));
            if (key != _rosterKey)
            {
                _rosterKey = key;
                BuildRoster(lobby, campaign);
            }
        }
    }

    private void BuildCampaignList(IReadOnlyList<CampaignSummary> campaigns)
    {
        foreach (var child in _campaignList.GetChildren())
        {
            child.QueueFree();
        }
        _campaignCards.Clear();
        if (campaigns.Count == 0)
        {
            // A room lists only campaigns a phone here vouches for (issue #60), so an empty list usually means "join first".
            _campaignList.AddChild(ArcTheme.Label("Your campaigns show here once a player joins on their phone", ArcTheme.SemiBold, 24,
                new Color(1, 1, 1, 0.22f)));
            return;
        }

        foreach (var campaign in campaigns)
        {
            var bar = new ColorRect { CustomMinimumSize = new Vector2(6, 0) };
            var name = ArcTheme.Label(campaign.Name, ArcTheme.Bold, 24);
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.ClipText = true;
            var meta = ArcTheme.Meta($"{campaign.Code} · {campaign.Members} MEMBER{(campaign.Members == 1 ? "" : "S")}", 13);
            var text = ArcTheme.VBox(0, name, meta);
            var inner = ArcTheme.Margin(0, 6, 14, 6, text);
            inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var card = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.HBox(14, bar, inner));
            card.MouseFilter = MouseFilterEnum.Stop;
            card.MouseDefaultCursorShape = CursorShape.PointingHand;
            foreach (var c in new Control[] { bar, name, meta, text, inner })
            {
                c.MouseFilter = MouseFilterEnum.Ignore;
            }
            var id = campaign.Id;
            card.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    CampaignSelected?.Invoke(id);
                }
            };
            _campaignList.AddChild(card);
            _campaignCards[id] = (card, name, bar);
        }
    }

    private void BuildRoster(LobbyState lobby, LobbyCampaign campaign)
    {
        foreach (var child in _roster.GetChildren())
        {
            child.QueueFree();
        }
        for (var i = 0; i < campaign.Roster.Count; i++)
        {
            var entry = campaign.Roster[i];
            var seated = entry.SeatedAs is not null && entry.Connected;
            // The card's own colour, else the seated player's colour from the players list, else one by roster position.
            var fallback = ArcTheme.PlayerColour(PlayerIndex(lobby, entry.SeatedAs) ?? i);
            var colour = new ColorRect
            {
                Color = string.IsNullOrWhiteSpace(entry.Card.Color) ? fallback : Color.FromString(entry.Card.Color, fallback),
                CustomMinimumSize = new Vector2(6, 36),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var title = ArcTheme.Label(entry.Card.Title, ArcTheme.Bold, 22, seated ? Colors.White : new Color(1, 1, 1, 0.45f));
            title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            title.ClipText = true;
            var text = ArcTheme.VBox(0, title);
            if (!string.IsNullOrWhiteSpace(entry.Card.Subtitle))
            {
                var subtitle = ArcTheme.Label(entry.Card.Subtitle, ArcTheme.SemiBold, 15, ArcTheme.Dim);
                subtitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                subtitle.ClipText = true;
                text.AddChild(subtitle);
            }
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.Alignment = BoxContainer.AlignmentMode.Center;

            var dot = new ColorRect
            {
                Color = seated ? ArcTheme.Good : ArcTheme.Dim,
                CustomMinimumSize = new Vector2(10, 10),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var state = ArcTheme.Meta(seated ? "SEATED" : "AWAY", 15, seated ? ArcTheme.Good : ArcTheme.Dim);
            state.CustomMinimumSize = new Vector2(76, 0);
            var status = ArcTheme.HBox(8, dot, state);
            status.SizeFlagsVertical = SizeFlags.ShrinkCenter;

            _roster.AddChild(ArcTheme.Panel(BottomEdge(Colors.Transparent, 24, 6), ArcTheme.HBox(14, colour, text, status)));
        }
    }

    /// <summary>Where a seated member sits in the players list, so the roster uses the same player colour.</summary>
    private static int? PlayerIndex(LobbyState lobby, PlayerId? seat)
    {
        if (seat is not { } id)
        {
            return null;
        }
        for (var i = 0; i < lobby.Players.Count; i++)
        {
            if (lobby.Players[i].Id == id)
            {
                return i;
            }
        }
        return null;
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
        if (game.CardArt is { } art)
        {
            ShowArt(card, art);
        }
        return (card, name, players, bar);
    }

    /// <summary>The largest a card's title image is drawn: pixel art, scaled in whole steps up to this.</summary>
    private const int MaxArtHeight = 96;
    private const int MaxArtWidth = 256;

    /// <summary>How strongly the emblem shows behind the card's text: enough to recognise, never enough to fight the words.</summary>
    private const float ArtAlpha = 0.3f;

    /// <summary>One loader per server, shared by every lobby screen: an image fetched once is kept (#405).</summary>
    private static readonly Dictionary<string, LobbyArt> ArtByServer = new(StringComparer.Ordinal);

    /// <summary>
    /// The game's title image, once it arrives (#405): an emblem behind the card's text, on its right. It's drawn on a
    /// layer of its own that layout ignores, so the card keeps its size and every line of text stays where it was (an
    /// image in the text column made the whole row taller and pushed the player counts off a 720p screen). The art
    /// carries no words (the art contract), so the name stays. A missing, slow or broken image leaves the card as it was.
    /// </summary>
    private async void ShowArt(PanelContainer card, string url)
    {
        if (!ArtByServer.TryGetValue(_serverUrl, out var loader))
        {
            loader = new LobbyArt(new System.Net.Http.HttpClient { BaseAddress = new Uri(_serverUrl + "/") });
            ArtByServer[_serverUrl] = loader;
        }
        var bytes = await loader.Get(url);   // resumes on the main thread (Godot's synchronization context)
        if (bytes is null || !IsInstanceValid(card))
        {
            return;   // no image, or the card was rebuilt meanwhile: the text card stands
        }
        var image = new Image();
        if (image.LoadPngFromBuffer(bytes) != Error.Ok || image.GetWidth() == 0 || image.GetHeight() == 0)
        {
            return;
        }
        var scale = Math.Max(1, Math.Min(MaxArtHeight / image.GetHeight(), MaxArtWidth / image.GetWidth()));
        var size = new Vector2(image.GetWidth() * scale, image.GetHeight() * scale);
        // A plain Control fills the card (the PanelContainer sizes it) but doesn't lay out its child, so the emblem can sit
        // anchored to the card's right edge, centred, without the card measuring it.
        var layer = new Control { Name = "CardArtLayer", MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };   // never past the card
        var art = new TextureRect
        {
            Name = "CardArt",
            Texture = ImageTexture.CreateFromImage(image),
            TextureFilter = TextureFilterEnum.Nearest,   // whole-step pixel art, never smoothed
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1, 1, 1, ArtAlpha),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -size.X - 12, OffsetRight = -12, OffsetTop = -size.Y / 2, OffsetBottom = size.Y / 2,
        };
        layer.AddChild(art);
        card.AddChild(layer);
        card.MoveChild(layer, 0);   // behind the card's text
        GD.Print($"LOBBY card art {url} at {scale}x");   // for the end-to-end run and screenshots
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
        else if (selected.UsesCampaigns && lobby.Campaign is null)
        {
            hint = "Pick a campaign first";
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
