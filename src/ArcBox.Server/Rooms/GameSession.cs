using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Microsoft.Extensions.Logging;

namespace ArcBox.Server.Rooms;

public sealed class GameSession : IDisposable
{
    /// <summary>A <see cref="CustomView"/> whose data is bigger than this (as JSON) is logged at Warning, and still sent.</summary>
    public const int CustomViewWarnBytes = 64 * 1024;

    private readonly IGame _game;
    private readonly ICampaignLease? _campaign;
    private readonly IReadOnlyList<Player> _players;
    private readonly Dictionary<PlayerId, Player> _playersById;
    private readonly TimeProvider _time;
    private readonly Action<Action> _postToRoom;
    private readonly ILogger _logger;
    private readonly SessionGameContext _context;

    private readonly Dictionary<string, TimerEntry> _timers = new();
    private readonly Dictionary<PlayerId, string> _lastPlayerContentJson = new();
    private readonly Dictionary<PlayerId, PlayerViewUpdate> _currentPlayerViews = new();
    private readonly Dictionary<PlayerId, PlayerViewUpdate> _pendingPlayerUpdates = new();

    private JsonElement? _currentHostView;
    private JsonElement? _pendingHost;
    private string? _lastHostJsonText;
    private int _viewCounter;
    private long _busySinceTicks;

    public GameSession(
        IGame game,
        IReadOnlyList<Player> players,
        TimeProvider time,
        int seed,
        Action<Action> postToRoom,
        ILogger logger,
        ICampaignLease? campaign = null)
    {
        _game = game;
        _campaign = campaign;
        _players = players;
        _playersById = players.ToDictionary(p => p.Id);
        _time = time;
        _postToRoom = postToRoom;
        _logger = logger;
        GameId = game.Manifest.Id;
        _context = new SessionGameContext(this, time, seed);
    }

    public string GameId { get; }
    public bool IsOver { get; private set; }
    public GameResults? Results { get; private set; }

    public DateTimeOffset? BusySince
    {
        get
        {
            long ticks = Interlocked.Read(ref _busySinceTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public event Action? TimerFired;

    public JsonElement? CurrentHostView => _currentHostView;

    public PlayerViewUpdate? CurrentPlayerView(PlayerId id)
    {
        return _currentPlayerViews.TryGetValue(id, out var update) ? update : null;
    }

    public void Start()
    {
        if (IsOver) return;

        // The lease stays the room's: it releases it when the game ends, however that happens.
        if (_campaign is not null && _game is ICampaignGame campaignGame)
        {
            Guard(() => campaignGame.Start(_context, _players, _campaign));
        }
        else
        {
            Guard(() => _game.Start(_context, _players));
        }

        if (!IsOver)
        {
            PublishViews();
        }
    }

    /// <summary>
    /// Starts a game whose room was rebuilt (spec §3.5): <see cref="IResumableGame.Resume"/> instead of Start, with every
    /// player disconnected. Like Start, the lease stays the room's.
    /// </summary>
    /// <exception cref="InvalidOperationException">The game isn't an <see cref="IResumableGame"/>; the room checks first.</exception>
    public void Resume()
    {
        if (IsOver) return;

        if (_game is not IResumableGame resumable)
        {
            throw new InvalidOperationException($"Game {GameId} can't be resumed.");
        }

        Guard(() => resumable.Resume(_context, _players, _campaign));

        if (!IsOver)
        {
            PublishViews();
        }
    }

    public bool Input(PlayerId from, string viewId, JsonElement payload)
    {
        if (IsOver) return false;
        if (!_playersById.ContainsKey(from)) return false;
        if (!_currentPlayerViews.TryGetValue(from, out var cur) || cur.ViewId != viewId) return false;

        Guard(() => _game.OnInput(from, new PlayerInput(payload)));

        if (!IsOver)
        {
            PublishViews();
        }

        return true;
    }

    /// <summary>
    /// Delivers a signal from the TV to a game that is <see cref="IHostAware"/>, guarded and published like input.
    /// Returns false, with a Debug log, when the game is over or doesn't listen for signals.
    /// </summary>
    public bool HostSignal(string kind, JsonElement payload)
    {
        if (IsOver) return false;

        if (_game is not IHostAware aware)
        {
            _logger.LogDebug("Game {GameId} isn't IHostAware; ignored host signal {Kind}", GameId, kind);
            return false;
        }

        Guard(() => aware.OnHostSignal(kind, new PlayerInput(payload)));

        if (!IsOver)
        {
            PublishViews();
        }

        return true;
    }

    public void ConnectionChanged(PlayerId id, bool connected)
    {
        if (IsOver) return;
        if (!_playersById.ContainsKey(id)) return;

        Guard(() => _game.OnPlayerConnectionChanged(id, connected));

        if (!IsOver)
        {
            PublishViews();
        }
    }

    public ViewUpdates TakeViewUpdates()
    {
        if (IsOver) return ViewUpdates.None;

        var host = _pendingHost;
        _pendingHost = null;

        var players = _pendingPlayerUpdates.Values.ToList();
        _pendingPlayerUpdates.Clear();

        return new ViewUpdates(host, players);
    }

    public void Dispose()
    {
        DisposeTimers();
    }

    private void PublishViews()
    {
        if (IsOver) return;

        var hostObj = GuardFunc(() => _game.HostView());
        if (IsOver) return;

        if (hostObj != null)
        {
            var hostElement = JsonSerializer.SerializeToElement(hostObj, hostObj.GetType(), ArcJson.Options);
            var hostJsonText = hostElement.GetRawText();
            if (_lastHostJsonText != hostJsonText)
            {
                _lastHostJsonText = hostJsonText;
                _currentHostView = hostElement;
                _pendingHost = hostElement;
            }
        }

        foreach (var player in _players)
        {
            var view = GuardFunc(() => _game.PlayerView(player.Id));
            if (IsOver) return;
            if (view == null) continue;

            var contentElement = JsonSerializer.SerializeToElement(view with { ViewId = "" }, ArcJson.Options);
            var contentJsonText = contentElement.GetRawText();

            if (!_lastPlayerContentJson.TryGetValue(player.Id, out var lastJson) || lastJson != contentJsonText)
            {
                _lastPlayerContentJson[player.Id] = contentJsonText;
                WarnIfOversized(view);
                var nextViewId = $"v{++_viewCounter}";
                var viewWithId = JsonSerializer.SerializeToElement(view with { ViewId = nextViewId }, ArcJson.Options);
                var update = new PlayerViewUpdate(player.Id, nextViewId, viewWithId);
                _currentPlayerViews[player.Id] = update;
                _pendingPlayerUpdates[player.Id] = update;
            }
        }
    }

    private void WarnIfOversized(ControllerView view)
    {
        if (view is not CustomView custom)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetByteCount(custom.Data.GetRawText());
        if (bytes > CustomViewWarnBytes)
        {
            _logger.LogWarning("Game {GameId} sent a CustomView for screen {Screen} with {Bytes} bytes of data; keep it under 64 KB",
                GameId, custom.Screen, bytes);
        }
    }

    private void FireTimer(string key, TimerEntry entry)
    {
        if (IsOver) return;

        if (!_timers.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
        {
            return;
        }

        _timers.Remove(key);
        entry.Dispose();

        Guard(() => _game.OnTimer(key));

        if (!IsOver)
        {
            PublishViews();
        }

        TimerFired?.Invoke();
    }

    private void Guard(Action action)
    {
        if (IsOver) return;

        Interlocked.Exchange(ref _busySinceTicks, _time.GetUtcNow().UtcTicks);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game {GameId} crashed", GameId);
            Results = new GameResults([], "Game crashed");
            IsOver = true;
            DisposeTimers();
        }
        finally
        {
            Interlocked.Exchange(ref _busySinceTicks, 0);
        }
    }

    private T? GuardFunc<T>(Func<T> func)
    {
        if (IsOver) return default;

        Interlocked.Exchange(ref _busySinceTicks, _time.GetUtcNow().UtcTicks);
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game {GameId} crashed", GameId);
            Results = new GameResults([], "Game crashed");
            IsOver = true;
            DisposeTimers();
            return default;
        }
        finally
        {
            Interlocked.Exchange(ref _busySinceTicks, 0);
        }
    }

    private void EndGame(GameResults results)
    {
        Results = results;
        IsOver = true;
        DisposeTimers();
    }

    private void DisposeTimers()
    {
        foreach (var entry in _timers.Values)
        {
            entry.Dispose();
        }
        _timers.Clear();
    }

    private sealed class TimerEntry : IDisposable
    {
        public ITimer? Timer { get; set; }
        public DateTimeOffset Deadline { get; init; }

        public void Dispose()
        {
            Timer?.Dispose();
        }
    }

    private sealed class SessionGameContext(GameSession session, TimeProvider time, int seed) : IGameContext
    {
        public DateTimeOffset Now => time.GetUtcNow();
        public Random Random { get; } = new(seed);

        public void SetTimer(string key, TimeSpan after)
        {
            if (session.IsOver) return;

            if (session._timers.TryGetValue(key, out var oldEntry))
            {
                session._timers.Remove(key);
                oldEntry.Dispose();
            }

            var entry = new TimerEntry
            {
                Deadline = time.GetUtcNow() + after
            };

            entry.Timer = time.CreateTimer(
                _ => session._postToRoom(() => session.FireTimer(key, entry)),
                null,
                after,
                Timeout.InfiniteTimeSpan
            );

            session._timers[key] = entry;
        }

        public void CancelTimer(string key)
        {
            if (session._timers.TryGetValue(key, out var entry))
            {
                session._timers.Remove(key);
                entry.Dispose();
            }
        }

        public DateTimeOffset? TimerDeadline(string key)
        {
            return session._timers.TryGetValue(key, out var entry) ? entry.Deadline : null;
        }

        public void EndGame(GameResults results)
        {
            session.EndGame(results);
        }
    }
}
