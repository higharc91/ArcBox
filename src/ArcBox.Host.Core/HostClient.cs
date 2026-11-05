using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Host.Core;

/// <summary>
/// The TV host's side of the hub (platform §5.3). Every server message becomes a <see cref="HostEvent"/> on
/// <see cref="Event"/>, raised on a pool thread. Reconnects automatically with the platform backoff and, once
/// reconnected, re-binds as host with the last room code and token; if that room is gone it raises
/// <see cref="RoomClosedEvent"/>.
/// </summary>
public sealed class HostClient : IAsyncDisposable
{
    /// <summary>Platform reconnect backoff (§5.4): 0, 1, 2, 5, 10 s, then 10 s forever.</summary>
    public static readonly IReadOnlyList<TimeSpan> ReconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly HubConnection _connection;
    private volatile CreateRoomResult? _room;
    private volatile DropHandler? _dropHandler;
    private int _disposed;
    private readonly Lock _viewLock = new();
    private JsonElement? _view;
    private string? _viewGame;

    /// <param name="server">Server base URL, e.g. <c>http://localhost:5080</c>; the hub is at <c>/hub</c>.</param>
    /// <param name="handler">Tests: an in-memory handler. When given, the client uses LongPolling through it.</param>
    public HostClient(Uri server, Func<HttpMessageHandler>? handler = null)
    {
        Server = server;
        var hubUrl = new Uri(WithTrailingSlash(server), "hub");
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                if (handler is not null)
                {
                    // Called once per underlying connection (start and every reconnect).
                    o.HttpMessageHandlerFactory = _ => _dropHandler = new DropHandler(handler());
                    o.Transports = HttpTransportType.LongPolling;
                }
            })
            .WithAutomaticReconnect(new PlatformRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(ArcJson.Options))
            .Build();

        _connection.On<LobbyState>(ClientMethods.LobbyChanged, l => Raise(new LobbyEvent(l)));
        _connection.On<string, JsonElement>(ClientMethods.HostView, (game, view) =>
        {
            lock (_viewLock)
                (_view, _viewGame) = (view, game);
            Raise(new HostViewEvent(game, view));
        });
        _connection.On<string, JsonElement>(ClientMethods.HostViewPatch, OnHostViewPatch);
        _connection.On<GameResults>(ClientMethods.GameEnded, r =>
        {
            ForgetView();
            Raise(new GameEndedEvent(r));
        });
        _connection.On<string, PackRef[]>(ClientMethods.ContentRequired, (game, packs) => Raise(new ContentRequiredEvent(game, packs)));
        _connection.On<string>(ClientMethods.RoomClosed, reason =>
        {
            _room = null;
            ForgetView();
            Raise(new RoomClosedEvent(reason));
        });

        _connection.Reconnecting += ex =>
        {
            Raise(new ConnectionEvent(HostConnectionState.Reconnecting, ex?.Message));
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ => ResumeAfterReconnectAsync();
        _connection.Closed += ex =>
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                Raise(new ConnectionEvent(HostConnectionState.Disconnected, ex?.Message));
            }
            return Task.CompletedTask;
        };
    }

    /// <summary>Raised on a SignalR pool thread; marshal to the main thread before touching UI.</summary>
    public event Action<HostEvent>? Event;

    public Uri Server { get; }

    /// <summary>The room this client hosts (after <see cref="CreateRoomAsync"/> or <see cref="ResumeHostAsync"/>).</summary>
    public CreateRoomResult? Room => _room;

    /// <summary>
    /// Whether to ask the server for the view as patches against the one this client has (issue #77), which keeps an overworld
    /// step in one network packet. <see cref="Event"/> still raises whole views either way. On by default.
    /// </summary>
    public bool UsePatches { get; init; } = true;

    /// <summary>Patches applied and whole views asked for again since this client started: for tests and the TV's perf log.</summary>
    public (int Applied, int Resent) PatchCounts => (Volatile.Read(ref _patchesApplied), Volatile.Read(ref _patchesResent));
    private int _patchesApplied;
    private int _patchesResent;

    /// <summary>How long <see cref="ConnectAsync"/> waits between failed attempts.</summary>
    public TimeSpan ConnectRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public HostConnectionState State => _connection.State switch
    {
        HubConnectionState.Connected => HostConnectionState.Connected,
        HubConnectionState.Connecting => HostConnectionState.Connecting,
        HubConnectionState.Reconnecting => HostConnectionState.Reconnecting,
        _ => HostConnectionState.Disconnected,
    };

    /// <summary>
    /// Connects, retrying every <see cref="ConnectRetryDelay"/> until it succeeds or <paramref name="ct"/> is cancelled.
    /// Each failure is reported as <see cref="ConnectionEvent"/>(<see cref="HostConnectionState.Disconnected"/>, error).
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Raise(new ConnectionEvent(HostConnectionState.Connecting, null));
            try
            {
                await _connection.StartAsync(ct);
                Raise(new ConnectionEvent(HostConnectionState.Connected, null));
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Raise(new ConnectionEvent(HostConnectionState.Disconnected, ex.Message));
            }

            await Task.Delay(ConnectRetryDelay, ct);
        }
    }

    public async Task<CreateRoomResult> CreateRoomAsync()
    {
        var room = await CallAsync(() => _connection.InvokeAsync<CreateRoomResult>("CreateRoom"));
        _room = room;
        await AskForPatchesAsync();
        return room;
    }

    /// <summary>Re-binds this connection as the room's host. Throws <see cref="HubException"/>("Room not found.") if it's gone.</summary>
    public async Task ResumeHostAsync(string code, string hostToken)
    {
        await CallAsync(() => _connection.InvokeAsync("ResumeHost", code, hostToken));
        _room = new CreateRoomResult(code.ToUpperInvariant(), hostToken);
        await AskForPatchesAsync();
    }

    public Task SelectGameAsync(string id) => CallAsync(() => _connection.InvokeAsync("SelectGame", id));

    /// <summary>
    /// Selects one of the selected game's campaigns (<see cref="LobbyState.Campaigns"/>); the lobby then carries its roster.
    /// Throws <see cref="HubException"/> with the room's message, e.g. "Campaign not found." or
    /// "{Name} is being played in room {CODE}.".
    /// </summary>
    public Task SelectCampaignAsync(string campaignId) => CallAsync(() => _connection.InvokeAsync("SelectCampaign", campaignId));

    public Task StartGameAsync() => CallAsync(() => _connection.InvokeAsync("StartGame"));

    public Task ReturnToLobbyAsync() => CallAsync(() => _connection.InvokeAsync("ReturnToLobby"));

    public Task KickAsync(string playerId) => CallAsync(() => _connection.InvokeAsync("Kick", playerId));

    /// <summary>
    /// Tells the running game something the TV saw, e.g. <c>SignalAsync("done", new { seq = 41 })</c>. The game may ignore
    /// it; the server drops signals for games that don't listen. Throws <see cref="HubException"/>("Invalid signal.") for a
    /// <paramref name="kind"/> outside <c>[A-Za-z0-9_.-]{1,32}</c> or a payload over 4096 bytes of JSON.
    /// </summary>
    public Task SignalAsync(string kind, object payload) =>
        CallAsync(() => _connection.InvokeAsync("HostSignal", kind, JsonSerializer.SerializeToElement(payload, ArcJson.Options)));

    /// <summary>Test hook: fails the in-flight transport requests as if the network dropped. Needs a test handler.</summary>
    internal Task SimulateDropAsync()
    {
        var drop = _dropHandler ?? throw new NotSupportedException("SimulateDropAsync needs the handler constructor argument.");
        drop.Drop();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// Asks for patches. Sent without waiting for an answer, so binding as host takes no longer than it did: the room handles it after
    /// the bind, in order. A server that doesn't offer patches keeps sending whole views, which works the same.
    /// </summary>
    private async Task AskForPatchesAsync()
    {
        if (!UsePatches)
            return;
        try { await _connection.SendAsync("UseHostPatches"); }
        catch (Exception ex) when (ex is HubException or InvalidOperationException) { /* Not connected: the next bind asks again. */ }
    }

    private void ForgetView()
    {
        lock (_viewLock)
            (_view, _viewGame) = (null, null);
    }

    /// <summary>
    /// Applies a patch to the view this client has and raises the whole view. One it can't apply (no view for that game yet, or a
    /// malformed patch) asks the server for the whole view again; the next <see cref="ClientMethods.HostView"/> puts things right.
    /// </summary>
    private void OnHostViewPatch(string game, JsonElement patch)
    {
        JsonElement? whole = null;
        lock (_viewLock)
        {
            if (_view is { } view && _viewGame == game)
            {
                try
                {
                    whole = HostViewPatch.Apply(view, patch);
                    (_view, _viewGame) = (whole, game);
                }
                catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or JsonException or FormatException)
                {
                    (_view, _viewGame) = (null, null);
                }
            }
        }

        if (whole is { } next)
        {
            Interlocked.Increment(ref _patchesApplied);
            Raise(new HostViewEvent(game, next));
            return;
        }

        Interlocked.Increment(ref _patchesResent);
        _ = ResendAsync();

        async Task ResendAsync()
        {
            try { await _connection.InvokeAsync("ResendHostView"); }
            catch (Exception) when (Volatile.Read(ref _disposed) == 0) { /* The reconnect's ResumeHost sends the whole view anyway. */ }
            catch (Exception) { }
        }
    }

    private async Task ResumeAfterReconnectAsync()
    {
        if (_room is { } room)
        {
            try
            {
                // The server answers with a fresh LobbyChanged (and HostView if a game runs): we redraw from that snapshot.
                await ResumeHostAsync(room.Code, room.HostToken);
            }
            catch (HubException ex)
            {
                _room = null;
                Raise(new ConnectionEvent(HostConnectionState.Connected, null));
                Raise(new RoomClosedEvent(ex.Message));
                return;
            }
            catch (Exception ex)
            {
                // Connection went again mid-call; the next Reconnected retries.
                Raise(new ConnectionEvent(HostConnectionState.Reconnecting, ex.Message));
                return;
            }
        }
        Raise(new ConnectionEvent(HostConnectionState.Connected, null));
    }

    private static async Task CallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (HubException ex)
        {
            throw new HubException(HubErrors.Clean(ex.Message), ex);
        }
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (HubException ex)
        {
            throw new HubException(HubErrors.Clean(ex.Message), ex);
        }
    }

    private void Raise(HostEvent e) => Event?.Invoke(e);

    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private sealed class PlatformRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            ReconnectDelays[(int)Math.Min(retryContext.PreviousRetryCount, ReconnectDelays.Count - 1)];
    }

    /// <summary>Lets <see cref="SimulateDropAsync"/> fail the transport's HTTP requests (LongPolling) on demand.</summary>
    private sealed class DropHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private TaskCompletionSource _drop = NewDrop();

        public void Drop() => Volatile.Read(ref _drop).TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var drop = Volatile.Read(ref _drop);
            if (drop.Task.IsCompleted)
            {
                // Dropped while no request was in flight: fail this one.
                Interlocked.CompareExchange(ref _drop, NewDrop(), drop);
                throw new HttpRequestException("Simulated connection drop.");
            }

            var send = base.SendAsync(request, cancellationToken);
            if (await Task.WhenAny(send, drop.Task) == drop.Task)
            {
                Interlocked.CompareExchange(ref _drop, NewDrop(), drop);
                _ = send.ContinueWith(static t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        t.Result.Dispose();
                    }
                }, TaskScheduler.Default);
                throw new HttpRequestException("Simulated connection drop.");
            }
            return await send;
        }

        private static TaskCompletionSource NewDrop() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>Turns SignalR's wrapped hub error text into the room's own message.</summary>
public static class HubErrors
{
    private const string Marker = "HubException: ";

    /// <summary>
    /// "An unexpected error occurred invoking 'ResumeHost' on the server. HubException: Room not found." → "Room not found."
    /// Text without the marker is returned unchanged.
    /// </summary>
    public static string Clean(string message)
    {
        var i = message.IndexOf(Marker, StringComparison.Ordinal);
        return i >= 0 ? message[(i + Marker.Length)..] : message;
    }
}
