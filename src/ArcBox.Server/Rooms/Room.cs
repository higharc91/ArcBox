using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace ArcBox.Server.Rooms;

public enum RoomStatus { Lobby, InGame, Closed }

public sealed class Room
{
    private readonly string _hostToken;
    private readonly IGameRegistry _registry;
    private readonly IClientSender _sender;
    private readonly TimeProvider _time;
    private readonly RoomOptions _options;
    private readonly ILogger<Room> _logger;

    private readonly Channel<Action> _inbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<string, byte> _knownConnections = new();
    private readonly RoomMembers _members;

    private volatile RoomStatus _status = RoomStatus.Lobby;
    private string? _hostConnectionId;
    private string? _selectedGameId;
    private GameSession? _session;
    private GameResults? _lastResults;

    private DateTimeOffset? _hostAbsentSince;
    private DateTimeOffset? _allAbsentSince;

    public Room(
        string code,
        string hostToken,
        IGameRegistry registry,
        IClientSender sender,
        TimeProvider time,
        RoomOptions options,
        ILogger<Room> logger)
    {
        Code = code;
        _hostToken = hostToken;
        _registry = registry;
        _sender = sender;
        _time = time;
        _options = options;
        _logger = logger;
        _members = new RoomMembers(options);

        var now = time.GetUtcNow();
        _hostAbsentSince = now;
        _allAbsentSince = now;
    }

    public string Code { get; }

    public DateTimeOffset? BusySince => _session?.BusySince;

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var reader = _inbox.Reader;
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing inbox message in room {Code}", Code);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            _status = RoomStatus.Closed;
            _session?.Dispose();
        }
    }

    public Task FlushAsync() => EnqueueAsync(() => { });

    public void ForceClose(string reason)
    {
        _status = RoomStatus.Closed;
        _session?.Dispose();

        foreach (var conn in _knownConnections.Keys)
        {
            try
            {
                _sender.Send(conn, ClientMethods.RoomClosed, reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send RoomClosed to {ConnectionId}", conn);
            }
        }

        _inbox.Writer.TryComplete();
    }

    public Task<RoomStatus> GetStatusAsync() => EnqueueAsync(() => _status);

    public Task BindHostAsync(string connectionId, string hostToken) => EnqueueAsync(() =>
    {
        if (!Secrets.Matches(_hostToken, hostToken))
        {
            throw new RoomException("Room not found.");
        }

        _hostConnectionId = connectionId;
        _knownConnections.TryAdd(connectionId, 0);
        _hostAbsentSince = null;
        _allAbsentSince = null;

        _sender.Send(connectionId, ClientMethods.LobbyChanged, GetLobbyState());

        if (_status == RoomStatus.InGame && _session?.CurrentHostView != null)
        {
            _sender.Send(connectionId, ClientMethods.HostView, _session.GameId, _session.CurrentHostView.Value);
        }
    });

    public Task<JoinResult> JoinAsync(string connectionId, string name) => EnqueueAsync(() =>
    {
        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("Game in progress, join after this round.");
        }

        if (!_members.TryAdd(name, connectionId, out var slot, out var error))
        {
            throw new RoomException(error!);
        }

        _knownConnections.TryAdd(connectionId, 0);
        _allAbsentSince = null;

        BroadcastLobby();

        return new JoinResult(Code, slot!.Id, slot.Secret);
    });

    public Task RejoinAsync(string connectionId, PlayerId playerId, string secret) => EnqueueAsync(() =>
    {
        var slot = _members.FindBySecret(playerId, secret);
        if (slot == null)
        {
            throw new RoomException("Couldn't rejoin. Please join again.");
        }

        bool wasConnected = slot.Connected;
        _members.Bind(slot, connectionId);
        _knownConnections.TryAdd(connectionId, 0);
        _allAbsentSince = null;

        _sender.Send(connectionId, ClientMethods.LobbyChanged, GetLobbyState());

        if (_status == RoomStatus.InGame && _session != null)
        {
            var playerView = _session.CurrentPlayerView(playerId);
            if (playerView != null)
            {
                _sender.Send(connectionId, ClientMethods.ControllerView, playerView.View);
            }

            if (!wasConnected)
            {
                _session.ConnectionChanged(playerId, true);
                Publish();
            }
        }

        BroadcastLobby();
    });

    public Task SelectGameAsync(string connectionId, string gameId) => EnqueueAsync(() =>
    {
        EnsureHostOrVip(connectionId);

        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("A game is already running.");
        }

        if (!_registry.Contains(gameId))
        {
            throw new RoomException("Unknown game.");
        }

        _selectedGameId = gameId;
        BroadcastLobby();
    });

    public Task StartGameAsync(string connectionId) => EnqueueAsync(() =>
    {
        EnsureHostOrVip(connectionId);

        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("A game is already running.");
        }

        if (_selectedGameId == null)
        {
            throw new RoomException("Pick a game first.");
        }

        var manifest = _registry.Manifests.First(m => m.Id == _selectedGameId);
        int max = Math.Min(manifest.MaxPlayers, _options.MaxPlayers);
        int min = manifest.MinPlayers;
        int count = _members.All.Count;

        if (count < min || count > max)
        {
            throw new RoomException($"This game needs {min}–{max} players.");
        }

        var vipId = _members.VipId;
        var players = _members.All
            .Select(s => new Player(s.Id, s.Name, IsVip: s.Id == vipId))
            .ToList();

        var game = _registry.Create(_selectedGameId);
        _session = new GameSession(game, players, _time, Random.Shared.Next(), Enqueue, _logger);
        _session.TimerFired += Publish;

        _status = RoomStatus.InGame;
        _session.Start();

        BroadcastLobby();
        Publish();
    });

    public Task ReturnToLobbyAsync(string connectionId) => EnqueueAsync(() =>
    {
        EnsureHost(connectionId);

        if (_status != RoomStatus.InGame || _session == null)
        {
            return;
        }

        EndGame(new GameResults([], "Game ended by host"));
    });

    public Task KickAsync(string connectionId, PlayerId target) => EnqueueAsync(() =>
    {
        EnsureHost(connectionId);

        if (_status != RoomStatus.Lobby)
        {
            throw new RoomException("Can only kick in the lobby.");
        }

        var slot = _members.Find(target);
        if (slot != null)
        {
            if (slot.Connected)
            {
                _sender.Send(slot.ConnectionId!, ClientMethods.Kicked);
            }
            _members.Remove(target);
            BroadcastLobby();
        }
    });

    public Task SendInputAsync(string connectionId, string viewId, JsonElement payload) => EnqueueAsync(() =>
    {
        if (_status != RoomStatus.InGame || _session == null)
        {
            return;
        }

        var slot = _members.FindByConnection(connectionId);
        if (slot == null)
        {
            return;
        }

        if (_session.Input(slot.Id, viewId, payload))
        {
            Publish();
        }
    });

    public Task ConnectionLostAsync(string connectionId) => EnqueueAsync(() =>
    {
        if (connectionId == _hostConnectionId)
        {
            _hostConnectionId = null;
            _hostAbsentSince = _time.GetUtcNow();
        }
        else
        {
            var slot = _members.Unbind(connectionId);
            if (slot != null)
            {
                if (_status == RoomStatus.InGame && _session != null)
                {
                    _session.ConnectionChanged(slot.Id, false);
                    Publish();
                }
                BroadcastLobby();
            }
        }

        if (_hostConnectionId == null && !_members.AnyConnected)
        {
            _allAbsentSince ??= _time.GetUtcNow();
        }
    });

    public Task<bool> IsExpiredAsync() => EnqueueAsync(() =>
    {
        var now = _time.GetUtcNow();
        bool hostExpired = _hostAbsentSince.HasValue && (now - _hostAbsentSince.Value) >= _options.IdleTimeout;
        bool allExpired = _allAbsentSince.HasValue && (now - _allAbsentSince.Value) >= _options.IdleTimeout;
        return hostExpired || allExpired;
    });

    private void EnsureHostOrVip(string connectionId)
    {
        bool isHost = connectionId == _hostConnectionId;
        bool isVip = _members.FindByConnection(connectionId)?.Id == _members.VipId;
        if (!isHost && !isVip)
        {
            throw new RoomException("Only the VIP or host can do that.");
        }
    }

    private void EnsureHost(string connectionId)
    {
        if (connectionId != _hostConnectionId)
        {
            throw new RoomException("Only the host can do that.");
        }
    }

    private void Publish()
    {
        if (_session == null) return;

        var updates = _session.TakeViewUpdates();
        if (updates.Host != null && _hostConnectionId != null)
        {
            _sender.Send(_hostConnectionId, ClientMethods.HostView, _session.GameId, updates.Host.Value);
        }

        foreach (var playerUpdate in updates.Players)
        {
            var slot = _members.Find(playerUpdate.PlayerId);
            if (slot != null && slot.Connected)
            {
                _sender.Send(slot.ConnectionId!, ClientMethods.ControllerView, playerUpdate.View);
            }
        }

        if (_session.IsOver)
        {
            EndGame(_session.Results ?? new GameResults([], "Game ended"));
        }
    }

    private void EndGame(GameResults results)
    {
        _session?.Dispose();
        _session = null;
        _status = RoomStatus.Lobby;
        _lastResults = results;

        if (_hostConnectionId != null)
        {
            _sender.Send(_hostConnectionId, ClientMethods.GameEnded, results);
        }

        foreach (var slot in _members.All.Where(s => s.Connected))
        {
            _sender.Send(slot.ConnectionId!, ClientMethods.GameEnded, results);
        }

        BroadcastLobby();
    }

    private LobbyState GetLobbyState()
    {
        var players = _members.ToLobby();
        bool inGame = _status == RoomStatus.InGame;
        bool canStart = false;

        if (!inGame && _selectedGameId != null)
        {
            var manifest = _registry.Manifests.FirstOrDefault(m => m.Id == _selectedGameId);
            if (manifest != null)
            {
                int max = Math.Min(manifest.MaxPlayers, _options.MaxPlayers);
                canStart = players.Count >= manifest.MinPlayers && players.Count <= max;
            }
        }

        return new LobbyState(
            Code,
            players,
            _selectedGameId,
            _registry.Manifests,
            inGame,
            canStart,
            _lastResults
        );
    }

    private void BroadcastLobby()
    {
        var state = GetLobbyState();
        if (_hostConnectionId != null)
        {
            _sender.Send(_hostConnectionId, ClientMethods.LobbyChanged, state);
        }

        foreach (var player in _members.All.Where(p => p.Connected))
        {
            _sender.Send(player.ConnectionId!, ClientMethods.LobbyChanged, state);
        }
    }

    private void Enqueue(Action action)
    {
        if (_status == RoomStatus.Closed || !_inbox.Writer.TryWrite(action))
        {
            _logger.LogDebug("Dropped action because room {Code} is closed", Code);
        }
    }

    private Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_status == RoomStatus.Closed)
        {
            tcs.TrySetException(new RoomException("Room has closed."));
            return tcs.Task;
        }

        bool written = _inbox.Writer.TryWrite(() =>
        {
            try
            {
                if (_status == RoomStatus.Closed)
                {
                    throw new RoomException("Room has closed.");
                }

                action();
                tcs.TrySetResult();
            }
            catch (RoomException ex)
            {
                tcs.TrySetException(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in room {Code}", Code);
                tcs.TrySetException(new RoomException("Something went wrong."));
            }
        });

        if (!written)
        {
            tcs.TrySetException(new RoomException("Room has closed."));
        }

        return tcs.Task;
    }

    private Task<T> EnqueueAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_status == RoomStatus.Closed)
        {
            tcs.TrySetException(new RoomException("Room has closed."));
            return tcs.Task;
        }

        bool written = _inbox.Writer.TryWrite(() =>
        {
            try
            {
                if (_status == RoomStatus.Closed)
                {
                    throw new RoomException("Room has closed.");
                }

                var res = func();
                tcs.TrySetResult(res);
            }
            catch (RoomException ex)
            {
                tcs.TrySetException(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in room {Code}", Code);
                tcs.TrySetException(new RoomException("Something went wrong."));
            }
        });

        if (!written)
        {
            tcs.TrySetException(new RoomException("Room has closed."));
        }

        return tcs.Task;
    }
}
