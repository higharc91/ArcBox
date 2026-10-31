using System.Text.Json;
using ArcBox.Host.Audio;
using ArcBox.Host.Core;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Godot;
using Microsoft.AspNetCore.SignalR;

namespace ArcBox.Host;

/// <summary>
/// Root of the TV app. Owns the <see cref="HostClient"/>, drains its events on the main thread every frame and
/// shows exactly one screen: Connect → Lobby → game scene → Results → Lobby. Everything shown comes from the
/// server's latest lobby / host view; the TV never decides anything. A game whose content packs aren't cached yet
/// waits behind <see cref="ContentLoadingCard"/> until they are (spec §3.6).
/// </summary>
public partial class Main : Control
{
    private enum ScreenKind { None, Connect, Lobby, Loading, Game, Results }

    private static readonly IReadOnlyDictionary<string, string> NoContent = new Dictionary<string, string>();

    private readonly MainThreadQueue _queue = new();
    private AppArgs _args = new();
    private SessionStore _store = null!;
    private Screenshotter? _shots;
    private HostClient? _client;
    private CancellationTokenSource? _connectCts;
    private int _generation;
    private string _serverUrl = HostSettings.DefaultServer;
    private SettingsStore _settings = null!;
    private string _preset = RenderPresets.Ultra;
    private AutoStart? _autoStart;
    private HostOverlay _overlay = null!;
    private Narrator _narrator = null!;

    /// <summary>Content packs (spec §3.6): one load per game, cached under <c>user://content</c>, fetched from the session's server.</summary>
    private ContentLoader _content = null!;
    private System.Net.Http.HttpClient? _contentHttp;
    private ContentCache? _contentCache;

    /// <summary>The selected game's scene, getting ready behind the lobby (<see cref="IPreloadScene"/>).</summary>
    private Node? _warm;
    private string? _warmGameId;
    private bool _warmReady;

    private Node? _screen;
    private ScreenKind _kind;
    private ConnectScreen? _connect;
    private LobbyScreen? _lobbyScreen;
    private ResultsScreen? _results;
    private ContentLoadingCard? _loading;
    private string? _loadingGameId;
    private IGameScene? _scene;
    private string? _sceneGameId;
    private JsonElement? _previousView;

    /// <summary>The newest view of the game waiting behind the loading card, rendered once its content is in.</summary>
    private HostViewEvent? _pendingView;

    /// <summary>The content load whose roots the scene on screen got (null: the game had no packs when it was shown).</summary>
    private ContentLoad? _sceneContent;

    private LobbyState? _lobby;
    private string? _lobbyShotKey;
    private bool _roomReady;
    private bool _wasReconnecting;
    private PanelContainer _banner = null!;
    private Label _bannerText = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = ArcTheme.Theme;
        // Godot caps a frame's process delta at max_physics_steps_per_frame physics ticks (default 8 / 60 s = 133 ms).
        // The TV's animations, Reveal follow-up cut and phase shots run on that delta while the server's phases run
        // on the wall clock, so after any frame longer than that the scene would fall behind the race. The TV has no
        // physics bodies, so allowing more steps costs nothing.
        Engine.MaxPhysicsStepsPerFrame = 120;
        MouseFilter = MouseFilterEnum.Ignore;

        _args = AppArgs.Parse(OS.GetCmdlineUserArgs());
        foreach (var unknown in _args.Unknown)
        {
            GD.PushWarning($"Ignoring unknown argument {unknown}");
        }
        if (_args.Resolution is var (w, h))
        {
            GetWindow().Size = new Vector2I(w, h);
        }
        if (_args.ShotsDir is { } dir)
        {
            _shots = new Screenshotter(dir);
            AddChild(_shots);
        }
        if (_args.QuitAfterSeconds is { } seconds)
        {
            GetTree().CreateTimer(seconds).Timeout += () => GetTree().Quit();
        }

        _content = new ContentLoader(EnsurePackAsync, _queue.Post);
        _store = new SessionStore(ProjectSettings.GlobalizePath("user://session.json"));
        _settings = new SettingsStore(ProjectSettings.GlobalizePath("user://settings.json"));
        var saved = _settings.Load();
        _serverUrl = (_args.Server ?? saved.ServerUrl ?? HostSettings.DefaultServer).TrimEnd('/');
        _preset = RenderPresets.Resolve(_args.Preset, saved.Preset);
        GD.Print($"Render preset {_preset}{(_args.Preset is not null ? " (--preset)" : saved.Preset is not null ? " (saved)" : "")}");
        if (_args.AutoStart is { } auto)
        {
            _autoStart = new AutoStart(auto);
            GD.Print($"Autostart: {auto.GameId} once {auto.MinPlayers} player(s) are in");
        }
        // Sound: the buses with the saved volumes, the shared narrator and the voice ducking.
        AudioLog.Enabled = _args.AudioLog;
        AudioBuses.Ensure();
        AudioBuses.Apply(AudioSettings.From(saved));
        _narrator = new Narrator { PreferredVoice = saved.NarrationVoice };
        AddChild(_narrator);
        AddChild(new Ducking { VoiceActive = () => _narrator.Speaking });

        BuildBanner();
        _overlay = new HostOverlay { Preset = _preset, LogPerf = _args.Perf, Narrator = _narrator };
        AddChild(_overlay);
        if (_args.Perf)
        {
            _overlay.TogglePerf();
        }

        if (_args.RedlineDemo)
        {
            // Dev only: a scripted REDLINE race through the normal host-view path, no server.
            GD.Print("REDLINE demo: no server connection");
            AddChild(new Games.Redline.Demo.RedlineDemoFeeder(view => OnHostView(new HostViewEvent("redline", view)), _args.RedlineDemoFrom, _args.RedlineDemoWinner));
            return;
        }
        if (_args.TacticsDemo is { } board)
        {
            // Dev only: a battle's board through the normal host-view path, no server; the art comes from the repository.
            GD.Print("Tactics demo: no server connection");
            ShowGame("tactics");
            var pack = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "content", "tactics", "base"));
            (_scene as IContentScene)?.SetContentRoots(new Dictionary<string, string> { ["base"] = pack });
            OnHostView(new HostViewEvent("tactics", JsonSerializer.SerializeToElement(global::Tactics.Presentation.Board.DemoBattle.View(board), ArcJson.Options)));
            return;
        }
        if (_args.Demo == AppArgs.DemoCampaignLobby)
        {
            // Dev only: the lobby of a campaign game with a built-in campaign list and roster, no server.
            GD.Print("Campaign lobby demo: no server connection");
            OnLobby(CampaignLobbyDemo.Lobby());
            return;
        }

        ShowConnect();
        _ = StartSessionAsync(_serverUrl, _args.NewRoom);
    }

    public override void _Process(double delta)
    {
        if (_warm is IPreloadScene { PreloadDone: true } && !_warmReady)
        {
            _warmReady = true;
            if (_lobby is { } lobby)
            {
                AutoStartStep(lobby);
            }
        }

        foreach (var e in _queue.Drain())
        {
            try
            {
                Handle(e);
            }
            catch (Exception ex)
            {
                GD.PushError($"Handling {e.GetType().Name} failed: {ex}");
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        // F2 / F3 work on every screen, even with a text field focused.
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.F2)
        {
            GetViewport().SetInputAsHandled();
            CyclePreset();
        }
        else if (key.Keycode == Key.F3)
        {
            GetViewport().SetInputAsHandled();
            _overlay.TogglePerf();
        }
        else if (key.Keycode == Key.M && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit))
        {
            GetViewport().SetInputAsHandled();
            ToggleMute();
        }
    }

    /// <summary>M: mute or unmute everything (music, effects, voice), saved for the next launch.</summary>
    private void ToggleMute()
    {
        var muted = !AudioBuses.Levels.Muted;
        AudioBuses.Apply(AudioBuses.Levels with { Muted = muted });
        _settings.Update(s => s with { Muted = muted });
        _overlay.Toast(muted ? "SOUND OFF" : "SOUND ON");
        GD.Print($"Sound {(muted ? "muted" : "on")} (M)");
    }

    /// <summary>F2: Ultra → High → Low → Ultra, applied to the scene on screen now and saved for the next launch.</summary>
    private void CyclePreset()
    {
        _preset = RenderPresets.Next(_preset);
        _settings.Update(s => s with { Preset = _preset });
        (_scene as IRenderPresetScene)?.SetRenderPreset(_preset);
        (_warm as IRenderPresetScene)?.SetRenderPreset(_preset);
        _overlay.Preset = _preset;
        _overlay.Toast($"PRESET · {RenderPresets.Title(_preset)}");
        _shots?.Capture($"preset-{_preset}");
        GD.Print($"Render preset {_preset} (F2)");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Host override: Esc during a game ends it and returns everyone to the lobby.
        if (_kind is (ScreenKind.Game or ScreenKind.Loading) && e.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            _ = CallAsync(c => c.ReturnToLobbyAsync());
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            DropWarm();
        }
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            _content?.Clear();
            _contentHttp?.Dispose();
            _contentHttp = null;
            _connectCts?.Cancel();
            _ = _client?.DisposeAsync().AsTask();
            _client = null;
        }
    }

    // ---- Session -------------------------------------------------------------------------------------------

    private async Task StartSessionAsync(string serverUrl, bool newRoom)
    {
        var generation = ++_generation;
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource();
        var ct = _connectCts.Token;
        await DropClientAsync();
        if (generation != _generation)
        {
            return;   // a newer session started while the old client was closing
        }

        _serverUrl = serverUrl.TrimEnd('/');
        _roomReady = false;
        _narrator.SetSession(null);
        _connect!.ServerUrl = _serverUrl;
        if (!Uri.TryCreate(_serverUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            _connect.SetStatus("That doesn't look like a server address", "Use something like http://192.168.1.20:5080", ArcTheme.Bad, false);
            return;
        }

        var client = new HostClient(uri);
        client.Event += _queue.Post;
        _client = client;
        ResetContent(client.Server);
        try
        {
            await client.ConnectAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation == _generation)
        {
            await OpenRoomAsync(generation, newRoom);
        }
    }

    /// <summary>Resume the saved room (platform §5.4) or create a new one; on a failed resume the file is deleted.</summary>
    private async Task OpenRoomAsync(int generation, bool newRoom)
    {
        while (generation == _generation && _client is { } client)
        {
            try
            {
                var saved = newRoom ? null : _store.Load();
                if (saved is not null && saved.ServerUrl.TrimEnd('/') == _serverUrl)
                {
                    try
                    {
                        _connect?.SetStatus("Resuming your room…", $"{_serverUrl} · room {saved.Code}", ArcTheme.Gold, true);
                        await client.ResumeHostAsync(saved.Code, saved.HostToken);
                        SetSpeechSession(saved.Code, saved.HostToken);
                        GD.Print($"ROOM {saved.Code}");
                        GD.Print($"Resumed room {saved.Code} from {_store.Path}");
                        return;
                    }
                    catch (HubException ex)
                    {
                        GD.Print($"Saved room {saved.Code} is gone ({ex.Message}); creating a new one");
                        _store.Clear();
                    }
                }

                _connect?.SetStatus("Opening a room…", _serverUrl, ArcTheme.Gold, true);
                var room = await client.CreateRoomAsync();
                _store.Save(new HostSession(_serverUrl, room.Code, room.HostToken));
                SetSpeechSession(room.Code, room.HostToken);
                GD.Print($"ROOM {room.Code}");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Connection dropped mid-call, or the server is full: say so and try again shortly.
                GD.PushWarning($"Opening a room failed: {ex.Message}");
                _connect?.SetStatus("Couldn't open a room", $"{HubErrors.Clean(ex.Message)} · retrying in 5 s", ArcTheme.Bad, true);
                newRoom = false;
                await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
            }
        }
    }

    /// <summary>The narrator asks this server's <c>/api/tts</c> as this room's host.</summary>
    private void SetSpeechSession(string code, string hostToken) =>
        _narrator.SetSession(new TtsSession(new Uri(_serverUrl + "/"), code, hostToken));

    private async Task DropClientAsync()
    {
        if (_client is { } old)
        {
            old.Event -= _queue.Post;
            _client = null;
            await old.DisposeAsync();
        }
    }

    private async Task CallAsync(Func<HostClient, Task> call)
    {
        if (_client is not { } client)
        {
            return;
        }
        try
        {
            await call(client);
        }
        catch (HubException ex)
        {
            _lobbyScreen?.Toast(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _lobbyScreen?.Toast("Not connected right now");
        }
    }

    // ---- Events --------------------------------------------------------------------------------------------

    private void Handle(HostEvent e)
    {
        switch (e)
        {
            case ConnectionEvent c:
                OnConnection(c);
                break;
            case LobbyEvent l:
                OnLobby(l.Lobby);
                break;
            case HostViewEvent v:
                OnHostView(v);
                break;
            case GameEndedEvent g:
                ShowResults(g.Results);
                break;
            case RoomClosedEvent r:
                OnRoomClosed(r.Reason);
                break;
            case ContentRequiredEvent c:
                OnContentRequired(c);
                break;
            case ContentLoadEvent l:
                OnContentLoad(l);
                break;
        }
    }

    private void OnConnection(ConnectionEvent c)
    {
        switch (c.State)
        {
            case HostConnectionState.Connecting:
                _connect?.SetStatus("Connecting…", _serverUrl, ArcTheme.Gold, true);
                break;
            case HostConnectionState.Connected:
                _connect?.SetStatus("Connected", _serverUrl, ArcTheme.Good, false);
                SetBanner(null);
                if (_wasReconnecting && _client?.Room is { } room)
                {
                    GD.Print($"ROOM {room.Code}");
                }
                _wasReconnecting = false;
                break;
            case HostConnectionState.Reconnecting:
                if (!_wasReconnecting)
                {
                    _wasReconnecting = true;
                    SetBanner("Connection lost · reconnecting…");
                    _shots?.Capture("reconnecting");
                }
                break;
            case HostConnectionState.Disconnected when !_roomReady:
                _connect?.SetStatus("Can't reach the server", $"{_serverUrl} · {Short(c.Error)} · retrying every 5 s", ArcTheme.Bad, true);
                break;
            case HostConnectionState.Disconnected:
                // The connection closed for good after we had a room: start over (this resumes the saved room).
                SetBanner(null);
                ShowConnect();
                _connect?.SetStatus("Connection closed", Short(c.Error), ArcTheme.Bad, true);
                _ = StartSessionAsync(_serverUrl, false);
                break;
        }
    }

    private void OnLobby(LobbyState lobby)
    {
        _lobby = lobby;
        _roomReady = true;
        GameSceneMap.Prewarm(lobby.SelectedGameId);

        if (lobby.InGame && lobby.SelectedGameId is { } gameId)
        {
            AutoStartStep(lobby);
            if (_kind != ScreenKind.Game || _sceneGameId != gameId)
            {
                ShowGame(gameId);
            }
            return;
        }

        // Off the game screen before warming up: a lobby that arrives while the game is still on air (a rebuilt room
        // after a server restart sends the lobby before its "interrupted" GameEnded) parks that scene as the warm one,
        // so WarmUp keeps it rather than loading a second copy behind it.
        switch (_kind)
        {
            case ScreenKind.Results:
                // ResultsScreen.Dismissed brings us back once the hold is over.
                break;
            case ScreenKind.Lobby:
                _lobbyScreen!.ShowLobby(lobby);
                LobbyShot();
                break;
            default:
                ShowLobby();
                break;
        }
        WarmUp(lobby.SelectedGameId);
        AutoStartStep(lobby);
    }

    /// <summary><c>--autostart</c>: select the game, then start it once enough players are in (first game only).</summary>
    private void AutoStartStep(LobbyState lobby)
    {
        var ready = _warm is not IPreloadScene { PreloadDone: false };
        switch (_autoStart?.Next(lobby, ready))
        {
            case AutoStartAction.Select:
                _ = CallAsync(c => c.SelectGameAsync(_autoStart.Spec.GameId));
                break;
            case AutoStartAction.Start:
                GD.Print($"Autostart: starting {_autoStart.Spec.GameId} with {lobby.Players.Count(p => p.Connected)} player(s)");
                _ = AutoStartAsync();
                break;
        }
    }

    private async Task AutoStartAsync()
    {
        try
        {
            await _client!.StartGameAsync();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Autostart failed: {ex.Message}");
            _autoStart?.StartFailed();
        }
    }

    private void OnHostView(HostViewEvent v)
    {
        if (_kind != ScreenKind.Game || _sceneGameId != v.GameId)
        {
            ShowGame(v.GameId);
        }

        if (_kind != ScreenKind.Game)
        {
            _pendingView = v;   // behind the loading card until the game's content is in
            return;
        }

        try
        {
            _scene!.Render(v.View, _previousView);
        }
        catch (Exception ex)
        {
            GD.PushError($"{v.GameId} scene failed to render: {ex}");
        }
        _previousView = v.View;
    }

    private void OnRoomClosed(string reason)
    {
        GD.Print($"Room closed: {reason}");
        _store.Clear();
        _narrator.SetSession(null);
        _roomReady = false;
        _lobby = null;
        ShowConnect();
        _connect?.SetStatus("The room closed", $"{reason} · opening a new room…", ArcTheme.Gold, true);
        _ = OpenRoomAsync(_generation, newRoom: true);
    }

    // ---- Content ---------------------------------------------------------------------------------------------

    /// <summary>A new session's server: one HTTP client for its content, and every earlier load forgotten (and cancelled).</summary>
    private void ResetContent(Uri server)
    {
        _content.Clear();
        _contentHttp?.Dispose();
        _contentHttp = new System.Net.Http.HttpClient { BaseAddress = new Uri(server.AbsoluteUri.TrimEnd('/') + "/") };
        _contentCache = new ContentCache(ProjectSettings.GlobalizePath("user://content"), _contentHttp);
    }

    /// <summary>
    /// A cached pack is answered here at once (so the loading card doesn't even flash); a download runs on the thread pool,
    /// so hashing and file writes stay off the main thread.
    /// </summary>
    private Task<string> EnsurePackAsync(string gameId, PackRef pack, IProgress<double> progress, CancellationToken ct)
    {
        var cache = _contentCache ?? throw new InvalidOperationException("No server to load content from.");
        return cache.IsCached(gameId, pack.PackId, pack.Hash)
            ? cache.EnsureAsync(gameId, pack.PackId, pack.Hash, progress, ct)
            : Task.Run(() => cache.EnsureAsync(gameId, pack.PackId, pack.Hash, progress, ct), ct);
    }

    private void OnContentRequired(ContentRequiredEvent c)
    {
        var load = _content.Require(c.GameId, c.Packs);
        if (load is { Ready: false, Error: null })
        {
            GD.Print($"Content for {c.GameId}: loading {string.Join(", ", load.Packs.Select(p => $"{p.PackId}@{p.Hash[..Math.Min(8, p.Hash.Length)]}"))}");
        }

        if (_kind == ScreenKind.Game && _sceneGameId == c.GameId && load is not null && load != _sceneContent)
        {
            // The game was shown before its content was known (a TV resuming a running game gets the lobby first).
            if (_previousView is not null)
            {
                GD.PushWarning($"Content for {c.GameId} changed while its scene was running; it keeps the content it has");
            }
            else if (load.Ready)
            {
                (_scene as IContentScene)?.SetContentRoots(load.Roots);
                _sceneContent = load;
            }
            else
            {
                ShowLoading(c.GameId);
            }
            return;
        }
        LoadingStep();
    }

    private void OnContentLoad(ContentLoadEvent e)
    {
        var load = _content.Apply(e);
        if (load is null)
        {
            return;   // an earlier attempt, or a game whose packs changed since
        }
        if (e is ContentFailedEvent failed)
        {
            GD.PushError($"Loading content pack '{failed.PackId}' of {failed.GameId} failed: {failed.Error}");
        }
        LoadingStep();
    }

    /// <summary>Moves the loading card on: its percentage, the failure, or on to the game once every pack is in.</summary>
    private void LoadingStep()
    {
        if (_kind != ScreenKind.Loading || _loadingGameId is not { } gameId)
        {
            return;
        }

        var load = _content.For(gameId);
        if (load is null || load.Ready)
        {
            // Every pack is cached (or the game turned out to need none): show it, with the newest view it was sent.
            var pending = _pendingView is { } v && v.GameId == gameId ? v : null;
            GD.Print($"Content for {gameId} ready");
            ShowGame(gameId);
            if (pending is not null)
            {
                OnHostView(pending);
            }
            return;
        }

        if (load.Error is not null)
        {
            _loading!.ShowFailed();
        }
        else
        {
            _loading!.ShowProgress(load.Percent);
        }
    }

    private void ShowLoading(string gameId)
    {
        if (_kind == ScreenKind.Loading && _loadingGameId == gameId)
        {
            LoadingStep();
            return;
        }

        var card = new ContentLoadingCard();
        card.Setup(_lobby?.Games.FirstOrDefault(g => g.Id == gameId)?.Name ?? gameId);
        card.RetryPressed += () =>
        {
            GD.Print($"Retrying content for {gameId}");
            _content.Retry(gameId);
            LoadingStep();
        };
        SetScreen(card, ScreenKind.Loading, $"{gameId}-loading");
        _loadingGameId = gameId;
        GD.Print($"Waiting for {gameId} content");
        LoadingStep();
    }

    // ---- Signals -------------------------------------------------------------------------------------------

    /// <summary><see cref="ISignalingScene.Signal"/>: fire and forget, through whichever client is current; failures are logged.</summary>
    private void SendSignal(string kind, object payload)
    {
        if (_client is not { } client)
        {
            GD.PushWarning($"Host signal '{kind}' dropped: not connected");
            return;
        }
        _ = SignalAsync(client, kind, payload);
    }

    private static async Task SignalAsync(HostClient client, string kind, object payload)
    {
        try
        {
            await client.SignalAsync(kind, payload);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Host signal '{kind}' failed: {ex.Message}");
        }
    }

    // ---- Screens -------------------------------------------------------------------------------------------

    private void SetScreen(Node screen, ScreenKind kind, string? shot)
    {
        if (_screen is IPreloadScene finished && _kind == ScreenKind.Game && _sceneGameId is { } finishedGame
            && screen != _screen)
        {
            // Keep the finished game's scene, ready for a rematch; WarmUp drops it if another game is picked.
            DropWarm();
            finished.Park();
            _warm = _screen;
            _warmGameId = finishedGame;
        }
        else if (_screen is not null && screen != _screen)
        {
            RemoveChild(_screen);
            _screen.QueueFree();
        }
        _connect = screen as ConnectScreen;
        _lobbyScreen = screen as LobbyScreen;
        _results = screen as ResultsScreen;
        _loading = screen as ContentLoadingCard;
        _scene = screen as IGameScene;
        if (kind != ScreenKind.Game)
        {
            _sceneGameId = null;
            _previousView = null;
            _sceneContent = null;
        }
        if (kind != ScreenKind.Loading)
        {
            _loadingGameId = null;
            _pendingView = null;
        }

        _screen = screen;
        _kind = kind;
        if (screen.GetParent() != this)
        {
            AddChild(screen);   // a scene warmed up behind the lobby is already in the tree
        }
        MoveChild(screen, 0);
        if (shot is not null)
        {
            _shots?.Capture(shot);
        }
    }

    private void ShowConnect()
    {
        if (_kind == ScreenKind.Connect)
        {
            return;
        }
        var screen = new ConnectScreen { ServerUrl = _serverUrl };
        screen.ConnectRequested += url =>
        {
            _settings.Update(s => s with { ServerUrl = url.TrimEnd('/') });
            _ = StartSessionAsync(url, false);
        };
        SetScreen(screen, ScreenKind.Connect, "connect");
    }

    private void ShowLobby()
    {
        var screen = new LobbyScreen();
        screen.SetServer(_serverUrl);
        screen.GameSelected += id => _ = CallAsync(c => c.SelectGameAsync(id));
        screen.CampaignSelected += id => _ = CallAsync(c => c.SelectCampaignAsync(id));
        screen.StartPressed += () =>
        {
            if (_lobby is { CanStart: true })
            {
                _ = CallAsync(c => c.StartGameAsync());
            }
        };
        if (_lobby is not null)
        {
            screen.ShowLobby(_lobby);
        }
        _lobbyShotKey = LobbyKey(_lobby);
        SetScreen(screen, ScreenKind.Lobby, "lobby");
        // Tools read it (the e2e restart run checks the lobby after the results is the same room's).
        GD.Print($"LOBBY {_lobby?.Code}");
    }

    /// <summary>Shots of lobby changes (players joining, game picked) while the lobby stays up.</summary>
    private void LobbyShot()
    {
        var key = LobbyKey(_lobby);
        if (key != _lobbyShotKey)
        {
            _lobbyShotKey = key;
            _shots?.Capture("lobby");
        }
    }

    private static string? LobbyKey(LobbyState? l) => l is null
        ? null
        : $"{l.Code}|{l.SelectedGameId}|{l.CanStart}|{string.Join(",", l.Players.Select(p => $"{p.Name}:{p.Connected}:{p.IsVip}"))}"
          + $"|{l.Campaigns?.Count}|{l.Campaign?.Id}|{string.Join(",", l.Campaign?.Roster.Select(r => $"{r.SeatedAs}:{r.Connected}") ?? [])}";

    /// <summary>Starts getting the selected game's scene ready behind the lobby, if it supports that.</summary>
    private void WarmUp(string? gameId)
    {
        if (gameId == _warmGameId)
        {
            return;
        }
        DropWarm();
        if (gameId is null)
        {
            return;
        }
        var node = LoadScene(GameSceneMap.ScenePath(gameId));
        if (node is not IPreloadScene preload)
        {
            node?.QueueFree();
            return;
        }
        PrepareScene(node);
        _warm = node;
        _warmGameId = gameId;
        AddChild(node);
        MoveChild(node, 0);
        preload.Preload();
    }

    private void DropWarm()
    {
        if (_warm is { } node && IsInstanceValid(node))
        {
            if (node.IsInsideTree())
            {
                RemoveChild(node);
            }
            node.QueueFree();
        }
        _warm = null;
        _warmGameId = null;
        _warmReady = false;
    }

    private static Node? LoadScene(string? path)
    {
        if (path is null || !ResourceLoader.Exists(path))
        {
            return null;
        }
        try
        {
            return GD.Load<PackedScene>(path)?.Instantiate();
        }
        catch (Exception ex)
        {
            GD.PushError($"Loading {path} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>What every game scene gets when it's created: the render preset, the narrator and the signal sender.</summary>
    private void PrepareScene(Node node)
    {
        (node as IRenderPresetScene)?.SetRenderPreset(_preset);
        (node as IAudioScene)?.SetNarrator(_narrator);
        if (node is ISignalingScene signaling)
        {
            signaling.Signal = SendSignal;
        }
    }

    private void ShowGame(string gameId)
    {
        var content = _content.For(gameId);
        if (content is { Ready: false })
        {
            ShowLoading(gameId);
            return;
        }

        var path = GameSceneMap.ScenePath(gameId);
        Node? node;
        if (_warm is not null && _warmGameId == gameId)
        {
            // The scene that got ready behind the lobby (or was kept from the last game).
            node = _warm;
            _warm = null;
            _warmGameId = null;
            _warmReady = false;
        }
        else
        {
            node = LoadScene(path);
        }

        var missing = node is not IGameScene;
        if (missing)
        {
            node?.QueueFree();
            var fallback = new MissingScene();
            fallback.Setup(gameId, path);
            node = fallback;
            GD.Print($"No TV scene for '{gameId}' ({path ?? "unmapped"}); showing the raw view");
        }

        ((IGameScene)node!).SetShots(_shots);
        PrepareScene(node!);
        (node as IContentScene)?.SetContentRoots(content?.Roots ?? NoContent);
        SetScreen(node!, ScreenKind.Game, missing ? $"{gameId}-missing" : gameId);
        (node as IPreloadScene)?.EndPreload();
        _sceneGameId = gameId;
        _sceneContent = content;
        _previousView = null;
    }

    private void ShowResults(GameResults results)
    {
        var gameName = _lobby?.Games.FirstOrDefault(g => g.Id == (_sceneGameId ?? _lobby.SelectedGameId))?.Name ?? "";
        var screen = new ResultsScreen();
        screen.Setup(results, gameName, _lobby?.Players.Select(p => p.Id).ToList() ?? []);
        screen.Dismissed += () =>
        {
            if (_kind == ScreenKind.Results && _lobby is { InGame: false })
            {
                ShowLobby();
            }
        };
        SetScreen(screen, ScreenKind.Results, "results");
        // Tools read it: `RESULTS <message> | <name> <score>, …`, either part left out when empty (the e2e restart run
        // checks for the "interrupted" note, the swap run that the swapped phone's player is listed once).
        string[] parts = [results.Message ?? "", string.Join(", ", results.Scores.Select(s => $"{s.Name} {s.Score}"))];
        GD.Print($"RESULTS {string.Join(" | ", parts.Where(p => p.Length > 0))}");
    }

    // ---- Reconnecting banner -------------------------------------------------------------------------------

    private void BuildBanner()
    {
        var layer = new CanvasLayer { Layer = 50 };
        AddChild(layer);
        // Sits in the 52 px top margin above every screen's header, so it never covers content.
        _bannerText = ArcTheme.Label("", ArcTheme.Bold, 20);
        var dot = new ColorRect { Color = ArcTheme.Red, CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var box = ArcTheme.Flat(new Color(ArcTheme.GlassSolid, 0.96f), 0, ArcTheme.Red, 0);
        box.BorderWidthLeft = 4;
        box.ContentMarginLeft = 16;
        box.ContentMarginRight = 18;
        box.ContentMarginTop = box.ContentMarginBottom = 3;
        _banner = ArcTheme.Panel(box, ArcTheme.HBox(12, dot, _bannerText));
        _banner.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _banner.GrowHorizontal = GrowDirection.Begin;
        _banner.OffsetRight = -64;
        _banner.OffsetLeft = -64;
        _banner.OffsetTop = 8;
        _banner.Visible = false;
        layer.AddChild(_banner);
    }

    private void SetBanner(string? text)
    {
        _banner.Visible = text is not null;
        _bannerText.Text = text ?? "";
    }

    private static string Short(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "no response";
        }
        var line = error.Split('\n')[0].Trim();
        return line.Length > 90 ? line[..90] + "…" : line;
    }
}
