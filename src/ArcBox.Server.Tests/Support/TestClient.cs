using System.Text.Json;
using System.Threading.Channels;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Server.Tests.Support;

public sealed record Received(string Method, JsonElement[] Args);

/// <summary>A SignalR client against <see cref="ArcServerFactory"/> that records every server→client message.</summary>
public sealed class TestClient : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Arity of each server→client method (spec §5.3).
    private static readonly Dictionary<string, int> Arity = new()
    {
        [ClientMethods.LobbyChanged] = 1,
        [ClientMethods.HostView] = 2,
        [ClientMethods.ControllerView] = 1,
        [ClientMethods.GameEnded] = 1,
        [ClientMethods.RoomClosed] = 1,
        [ClientMethods.Kicked] = 0,
        [ClientMethods.ContentRequired] = 2,
    };

    private readonly HubConnection _connection;
    private readonly Channel<Received> _inbox = Channel.CreateUnbounded<Received>();
    private readonly List<Received> _seen = [];
    private bool _disposed;

    private TestClient(HubConnection connection)
    {
        _connection = connection;
        foreach (var (method, arity) in Arity)
        {
            _connection.On(method, Enumerable.Repeat(typeof(JsonElement), arity).ToArray(), (args, _) =>
            {
                _inbox.Writer.TryWrite(new Received(method, [.. args.Cast<JsonElement>()]));
                return Task.CompletedTask;
            }, null!);
        }
    }

    public static async Task<TestClient> ConnectAsync<TEntry>(WebApplicationFactory<TEntry> factory) where TEntry : class
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hub"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = new JsonSerializerOptions(ArcJson.Options))
            .Build();
        var client = new TestClient(connection);
        await connection.StartAsync();
        return client;
    }

    public string? ConnectionId => _connection.ConnectionId;

    public Task<RoomPeek> PeekAsync(string code, IReadOnlyList<string>? profileKeys = null) =>
        InvokeAsync<RoomPeek>("Peek", new PeekRequest(code, profileKeys));

    public Task<JoinResult> TakeSeatAsync(TakeSeatRequest request) => InvokeAsync<JoinResult>("TakeSeat", request);

    public Task InvokeAsync(string method, params object?[] args) => _connection.InvokeCoreAsync(method, args);

    public Task<T> InvokeAsync<T>(string method, params object?[] args) => _connection.InvokeCoreAsync<T>(method, args);

    /// <summary>Invokes <paramref name="method"/>, expecting a <see cref="HubException"/>; returns its message.</summary>
    public async Task<string> ThrowsHubAsync(string method, params object?[] args)
    {
        var ex = await Assert.ThrowsAsync<HubException>(() => _connection.InvokeCoreAsync(method, args));
        return HubMessage(ex);
    }

    /// <summary>Null on success, or the <see cref="HubException"/> message.</summary>
    public async Task<string?> TryInvokeMessageAsync(string method, params object?[] args)
    {
        try
        {
            await _connection.InvokeCoreAsync(method, args);
            return null;
        }
        catch (HubException ex)
        {
            return HubMessage(ex);
        }
    }

    /// <summary>The last argument of the first not-yet-taken <paramref name="method"/> message that matches.</summary>
    public async Task<JsonElement> WaitForAsync(string method, Func<JsonElement, bool>? match = null)
    {
        var args = await WaitForArgsAsync(method, match is null ? null : a => a.Length > 0 && match(a[^1]));
        return args.Length > 0 ? args[^1] : default;
    }

    /// <summary>Every argument of the first not-yet-taken <paramref name="method"/> message that matches; 5 s timeout.</summary>
    public async Task<JsonElement[]> WaitForArgsAsync(string method, Func<JsonElement[], bool>? match = null)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            var i = _seen.FindIndex(r => r.Method == method && Matches(match, r.Args));
            if (i >= 0)
            {
                var hit = _seen[i];
                _seen.RemoveAt(i);
                return hit.Args;
            }

            try
            {
                _seen.Add(await _inbox.Reader.ReadAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                var got = string.Join(", ", _seen.Select(r => r.Method));
                throw new TimeoutException($"No matching {method} within {Timeout.TotalSeconds}s. Unclaimed: [{got}]");
            }
        }
    }

    /// <summary>
    /// How many <paramref name="method"/> messages have arrived and not been taken by a WaitFor. Messages on one
    /// connection arrive in order, so after waiting for a later message this counts everything sent before it.
    /// </summary>
    public int Unclaimed(string method)
    {
        while (_inbox.Reader.TryRead(out var r))
        {
            _seen.Add(r);
        }
        return _seen.Count(r => r.Method == method);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _connection.DisposeAsync();
    }

    private static bool Matches(Func<JsonElement[], bool>? match, JsonElement[] args)
    {
        if (match is null) return true;
        try
        {
            return match(args);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    // The server sends "An unexpected error occurred invoking 'X' on the server. HubException: <message>".
    private static string HubMessage(HubException ex)
    {
        const string marker = "HubException: ";
        var i = ex.Message.IndexOf(marker, StringComparison.Ordinal);
        return i >= 0 ? ex.Message[(i + marker.Length)..] : ex.Message;
    }
}
