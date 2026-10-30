using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Hubs;
using ArcBox.Server.Profiles;
using Microsoft.Extensions.Logging;

namespace ArcBox.Server.Rooms;

public enum RoomStatus { Lobby, InGame, Closed }

public sealed partial class Room
{
    /// <summary>The most UTF-8 bytes a host signal's payload may take, as raw JSON text.</summary>
    public const int MaxSignalPayloadBytes = 4096;

    // \z rather than $, which would also let a trailing newline through.
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,32}\z")]
    private static partial Regex SignalKind { get; }

    private readonly string _hostTokenHash;
    private readonly IGameRegistry _registry;
    private readonly IClientSender _sender;
    private readonly TimeProvider _time;
    private readonly RoomOptions _options;
    private readonly ILogger<Room> _logger;
    private readonly ProfileService _profiles;
    private readonly CampaignService _campaigns;
    private readonly CampaignRulesRegistry _rules;
    private readonly RoomStore _store;
    private readonly ContentPackRegistry _content;
    private readonly DateTimeOffset _createdUtc;

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

    public Room(string code, string hostToken, RoomServices services)
        : this(code, Secrets.Hash(hostToken), services, services.Time.GetUtcNow())
    {
    }

    private Room(string code, string hostTokenHash, RoomServices services, DateTimeOffset createdUtc)
    {
        Code = code;
        _hostTokenHash = hostTokenHash;
        _createdUtc = createdUtc;
        _store = services.Store;
        _content = services.Content;
        _registry = services.Registry;
        _sender = services.Sender;
        _time = services.Time;
        _options = services.Options;
        _logger = services.Loggers.CreateLogger<Room>();
        _profiles = services.Profiles;
        _campaigns = services.Campaigns;
        _rules = services.Rules;
        _members = new RoomMembers(_options);

        var now = _time.GetUtcNow();
        _hostAbsentSince = now;
        _allAbsentSince = now;
    }

    public string Code { get; }

    public DateTimeOffset? BusySince => _session?.BusySince;

    /// <summary>
    /// Constant-time check of the host token. Safe to call from any thread without the actor: the token is set in the
    /// constructor and never changes.
    /// </summary>
    public bool IsHostToken(string? token) => token is not null && Secrets.Matches(_hostTokenHash, token);

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
            ReleaseCampaign();
        }
    }

    public Task FlushAsync() => EnqueueAsync(() => { });

    public void ForceClose(string reason)
    {
        _status = RoomStatus.Closed;
        _session?.Dispose();
        ReleaseCampaign();

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
        if (!Secrets.Matches(_hostTokenHash, hostToken))
        {
            throw new RoomException("Room not found.");
        }

        _hostConnectionId = connectionId;
        _knownConnections.TryAdd(connectionId, 0);
        _hostAbsentSince = null;
        _allAbsentSince = null;

        _sender.Send(connectionId, ClientMethods.LobbyChanged, GetLobbyState());
        SendContentRequired();
        if (TakeHostNotice() is { } notice)
        {
            _sender.Send(connectionId, ClientMethods.GameEnded, notice);
        }

        if (_status == RoomStatus.InGame && _session?.CurrentHostView != null)
        {
            _sender.Send(connectionId, ClientMethods.HostView, _session.GameId, _session.CurrentHostView.Value);
        }
    });

    // --- Seats: how a phone gets into the room (spec §3.1-3.2) ---

    /// <summary>What a phone with these profile keys may take. Creates and changes nothing.</summary>
    public Task<RoomPeek> PeekAsync(IReadOnlyList<string> profileKeys) => EnqueueAsync(() =>
    {
        bool inGame = _status == RoomStatus.InGame;
        var campaign = SelectedCampaign();

        // Without a campaign every offline seat is on offer, in join order; a campaign room offers its members instead.
        List<Offer> offers = campaign is null
            ? [.. _members.Offline.Select(s => new Offer(
                new SeatOffer(new SeatRef(Slot: s.Id), new SeatCard(s.Name), Absent: inGame && s.AbsentAtStart), s.Profile))]
            : CampaignOffers(campaign, inGame);

        return new RoomPeek(Code, _selectedGameId, campaign?.Info.Name, inGame,
            [.. offers.Select(o => o.Seat)], Suggest(offers, profileKeys), CanJoinNew: !inGame);
    });

    /// <summary>Takes a seat: either a new one under <c>NewName</c>, or an existing offline one.</summary>
    public Task<JoinResult> TakeSeatAsync(string connectionId, TakeSeatRequest request) => EnqueueAsync(() =>
    {
        var result = TakeSeat(connectionId, request);
        Save();
        return result;
    });

    private JoinResult TakeSeat(string connectionId, TakeSeatRequest request)
    {
        if ((request.Seat is null) == (request.NewName is null))
        {
            throw new RoomException("Pick a seat or enter a name.");
        }

        var keys = request.ProfileKeys ?? [];
        if (request.NewName is not null)
        {
            return TakeNewSeat(connectionId, request.NewName);
        }

        var seat = OneSeat(request.Seat!);
        return seat.Member is { } member
            ? TakeMemberSeat(connectionId, member, keys)
            : TakeOfflineSeat(connectionId, seat.Slot!.Value, keys);
    }

    /// <summary>The pre-picker call; a stale phone tab still uses it.</summary>
    public Task<JoinResult> JoinAsync(string connectionId, string name) =>
        TakeSeatAsync(connectionId, new TakeSeatRequest(Code, NewName: name));

    public Task RejoinAsync(string connectionId, PlayerId playerId, string secret) => EnqueueAsync(() =>
    {
        var slot = _members.FindBySecret(playerId, secret);
        if (slot == null)
        {
            throw new RoomException("Couldn't rejoin. Please join again.");
        }

        SeatConnection(connectionId, slot);
    });

    private JoinResult TakeNewSeat(string connectionId, string name)
    {
        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("Game in progress, join after this round.");
        }

        if (SelectedCampaign() is { } campaign)
        {
            return TakeNewMemberSeat(connectionId, name, campaign);
        }

        if (!_members.TryAdd(name, connectionId, out var slot, out var secret, out var error))
        {
            throw new RoomException(error!);
        }

        return Seated(connectionId, slot!, secret!, profileKey: null);
    }

    /// <summary>A phone has a new seat: the room knows it, and everyone's lobby shows it.</summary>
    private JoinResult Seated(string connectionId, PlayerSlot slot, string secret, ProfileKey? profileKey)
    {
        _knownConnections.TryAdd(connectionId, 0);
        _allAbsentSince = null;

        BroadcastLobby();

        return new JoinResult(Code, slot.Id, secret, profileKey);
    }

    private JoinResult TakeOfflineSeat(string connectionId, PlayerId id, IReadOnlyList<string> profileKeys)
    {
        var slot = _members.Find(id);
        if (slot == null)
        {
            throw new RoomException("Couldn't rejoin. Please join again.");
        }

        // The inbox runs one message at a time, so of two phones tapping the same seat the second one sees it taken.
        if (slot.Connected)
        {
            throw new RoomException($"Someone's already playing as {TitleOf(slot)}.");
        }

        // A phone sits in one seat: one that holds another seat moves, leaving it as a lost connection would.
        if (_members.FindByConnection(connectionId) != null)
        {
            Unseat(connectionId);
        }

        // The old phone's secret stops working; if it comes back, its Rejoin fails and it lands on the picker.
        var secret = _members.RotateSecret(slot);
        SeatConnection(connectionId, slot);
        return new JoinResult(Code, slot.Id, secret, KeyFor(slot.Profile, profileKeys));
    }

    /// <summary>Binds <paramref name="connectionId"/> to an existing seat and brings the phone up to date.</summary>
    private void SeatConnection(string connectionId, PlayerSlot slot)
    {
        bool wasConnected = slot.Connected;
        _members.Bind(slot, connectionId);
        slot.AbsentAtStart = false; // someone has it now, by Rejoin or by taking it
        if (slot.ForAbsentMember)
        {
            slot.ForAbsentMember = false; // the member is here now: the seat is theirs whatever is selected next
            Save();
        }

        _knownConnections.TryAdd(connectionId, 0);
        _allAbsentSince = null;

        _sender.Send(connectionId, ClientMethods.LobbyChanged, GetLobbyState());
        if (TakeSlotNotice(slot.Id) is { } notice)
        {
            _sender.Send(connectionId, ClientMethods.GameEnded, notice);
        }

        if (_status == RoomStatus.InGame && _session != null)
        {
            var playerView = _session.CurrentPlayerView(slot.Id);
            if (playerView != null)
            {
                _sender.Send(connectionId, ClientMethods.ControllerView, playerView.View);
            }

            if (!wasConnected)
            {
                _session.ConnectionChanged(slot.Id, true);
                Publish();
            }
        }

        BroadcastLobby();
    }

    /// <summary>
    /// Tells the host which content packs the selected game needs (spec §3.6): always sent, with an empty list for a game
    /// that has none. Nothing is sent without a host or a selected game.
    /// </summary>
    private void SendContentRequired()
    {
        if (_hostConnectionId is not { } host || _selectedGameId is not { } gameId) return;

        PackRef[] packs = [.. _content.For(gameId).Select(p => new PackRef(p.PackId, p.Hash))];
        _sender.Send(host, ClientMethods.ContentRequired, gameId, packs);
    }

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

        // The registry ignores case; the manifest's own id is what everything else compares with.
        var id = _registry.Manifests.First(m => string.Equals(m.Id, gameId, StringComparison.OrdinalIgnoreCase)).Id;
        if (id != _selectedGameId)
        {
            SetCampaign(null);
        }

        _selectedGameId = id;
        SendContentRequired();
        BroadcastLobby();
        Save();
    });

    public Task StartGameAsync(string connectionId) => EnqueueAsync(() =>
    {
        EnsureHostOrVip(connectionId);

        if (_status == RoomStatus.InGame)
        {
            throw new RoomException("A game is already running.");
        }

        var campaign = SelectedCampaign();
        if (CannotStart(campaign) is { } problem)
        {
            throw new RoomException(problem);
        }

        var game = _registry.Create(_selectedGameId!);
        var lease = campaign is null ? null : CheckOutForStart(campaign);

        // Ahead of the first HostView, so the TV can show its loading card before the game.
        SendContentRequired();

        _session = new GameSession(game, Players(), _time, Random.Shared.Next(), Enqueue, _logger, lease);
        _session.TimerFired += Publish;

        _status = RoomStatus.InGame;
        ForgetNotice();
        _session.Start();

        // Every member is a player from the start; the game hears about the ones who aren't here as it would a lost phone.
        if (lease is not null)
        {
            foreach (var slot in _members.All.Where(s => !s.Connected))
            {
                _session.ConnectionChanged(slot.Id, false);
            }
        }

        BroadcastLobby();
        Publish();
        Save();
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
            Save();
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

    /// <summary>
    /// A signal from the TV for the running game (spec §3.7). Host only; the <paramref name="kind"/> and payload size are
    /// checked after that. With no game running, or one that isn't <see cref="IHostAware"/>, it is dropped.
    /// </summary>
    public Task HostSignalAsync(string connectionId, string kind, JsonElement payload) => EnqueueAsync(() =>
    {
        EnsureHost(connectionId);

        if (kind is null || !SignalKind.IsMatch(kind)
            || Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaxSignalPayloadBytes)
        {
            throw new RoomException("Invalid signal.");
        }

        if (_status != RoomStatus.InGame || _session == null)
        {
            _logger.LogDebug("Dropped host signal {Kind} in room {Code}: no game is running", kind, Code);
            return;
        }

        if (_session.HostSignal(kind, payload))
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
            Unseat(connectionId);
        }

        if (_hostConnectionId == null && !_members.AnyConnected)
        {
            _allAbsentSince ??= _time.GetUtcNow();
        }
    });

    /// <summary>Frees the seat <paramref name="connectionId"/> holds, telling the game and everyone's lobby.</summary>
    private void Unseat(string connectionId)
    {
        var slot = _members.Unbind(connectionId);
        if (slot == null)
        {
            return;
        }

        if (_status == RoomStatus.InGame && _session != null)
        {
            _session.ConnectionChanged(slot.Id, false);
            Publish();
        }

        BroadcastLobby();
    }

    public Task<bool> IsExpiredAsync() => EnqueueAsync(IsExpired);

    /// <summary>
    /// Whether the room has been idle for <see cref="RoomOptions.IdleTimeout"/>. Only a room with no connection bound at
    /// all is idle: phones still in it keep it live however long the TV is away (spec §3.5, a long break is harmless).
    /// </summary>
    private bool IsExpired()
    {
        if (_hostConnectionId is not null || _members.AnyConnected)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        bool hostExpired = _hostAbsentSince.HasValue && (now - _hostAbsentSince.Value) >= _options.IdleTimeout;
        bool allExpired = _allAbsentSince.HasValue && (now - _allAbsentSince.Value) >= _options.IdleTimeout;
        return hostExpired || allExpired;
    }

    private void EnsureHostOrVip(string connectionId)
    {
        bool isHost = connectionId == _hostConnectionId;
        // With nobody connected there is no VIP; without this check a connection bound to
        // no slot would match it as null == null.
        var vipId = _members.VipId;
        bool isVip = vipId is not null && _members.FindByConnection(connectionId)?.Id == vipId;
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
        ReleaseCampaign();
        foreach (var slot in _members.All)
        {
            slot.AbsentAtStart = false;
        }
        Save();

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

    /// <summary>Every seat as a player of the game about to begin; the VIP is the first seat with a phone in it.</summary>
    private List<Player> Players()
    {
        var vipId = _members.VipId;
        return [.. _members.All.Select(s => new Player(s.Id, s.Name, IsVip: s.Id == vipId, s.Profile))];
    }

    private GameManifest? SelectedManifest =>
        _selectedGameId is null ? null : _registry.Manifests.FirstOrDefault(m => m.Id == _selectedGameId);

    /// <summary>
    /// Why the selected game can't start now, or null. In a campaign room every member gets a seat, so the player
    /// count is the member count, absent members included.
    /// </summary>
    private string? CannotStart(CampaignView? campaign)
    {
        var manifest = SelectedManifest;
        if (manifest == null)
        {
            return "Pick a game first.";
        }

        int count = _members.All.Count;
        if (manifest.UsesCampaigns)
        {
            if (campaign is null)
            {
                return "Pick a campaign first.";
            }

            if (_members.All.Any(s => s.Connected && !campaign.Has(s.Profile)))
            {
                return "Everyone needs to pick who they are first.";
            }

            count = campaign.Info.Members.Count;
        }

        int max = Math.Min(manifest.MaxPlayers, _options.MaxPlayers);
        int min = manifest.MinPlayers;
        return count < min || count > max ? $"This game needs {min}–{max} players." : null;
    }

    private LobbyState GetLobbyState()
    {
        bool inGame = _status == RoomStatus.InGame;
        var campaign = SelectedCampaign();

        return new LobbyState(
            Code,
            _members.ToLobby(),
            _selectedGameId,
            _registry.Manifests,
            inGame,
            CanStart: !inGame && CannotStart(campaign) is null,
            _lastResults,
            campaign is null ? null : new LobbyCampaign(campaign.Info.Id, campaign.Info.Code, campaign.Info.Name, Roster(campaign)),
            SelectedManifest is { UsesCampaigns: true } ? _campaigns.ListForGame(_selectedGameId!) : null
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
            catch (CampaignException ex)
            {
                // The campaign service's refusals are written for the player, like the room's own.
                tcs.TrySetException(new RoomException(ex.Message));
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
            catch (CampaignException ex)
            {
                // The campaign service's refusals are written for the player, like the room's own.
                tcs.TrySetException(new RoomException(ex.Message));
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
