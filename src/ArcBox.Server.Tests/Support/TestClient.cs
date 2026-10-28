using System.Text.Json;
using System.Threading.Channels;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Server.Tests.Support;

public sealed class TestClient : IAsyncDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    // Argument count of every server→client method (spec §5.3).
    private static readonly Dictionary<string, int> Arities = new()
    {
        [ClientMethods.LobbyChanged] = 1,
        [ClientMethods.HostView] = 2,
        [ClientMethods.ControllerView] = 1,
        [ClientMethods.GameEnded] = 1,
        [ClientMethods.RoomClosed] = 1,
        [ClientMethods.Kicked] = 0,
    };

    private readonly HubConnection _connection;
    private readonly Channel<Received> _received = Channel.CreateUnbounded<Received>();

    private TestClient(HubConnection connection)
    {
        _connection = connection;
        foreach (var (method, arity) in Arities)
        {
            var parameterTypes = Enumerable.Repeat(typeof(JsonElement), arity).ToArray();
            _connection.On(method, parameterTypes, (args, state) =>
            {
                _received.Writer.TryWrite(new Received((string)state, args.Select(a => (JsonElement)a!).ToArray()));
                return Task.CompletedTask;
            }, method);
        }
    }

    public static async Task<TestClient> ConnectAsync(ArcServerFactory factory)
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
        await connection.StartAsync(TestContext.Current.CancellationToken);
        return client;
    }

    /// <summary>
    /// Reads forward through the messages received so far, skipping any that don't match,
    /// and returns the payload (last argument) of the first one that does.
    /// </summary>
    public async Task<JsonElement> WaitForAsync(string method, Func<JsonElement, bool>? match = null)
    {
        var testCancelled = TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(testCancelled);
        timeout.CancelAfter(WaitTimeout);

        var skipped = new List<string>();
        try
        {
            while (true)
            {
                var message = await _received.Reader.ReadAsync(timeout.Token);
                if (message.Method == method && Matches(match, message.Payload))
                {
                    return message.Payload;
                }
                skipped.Add(message.Method);
            }
        }
        catch (OperationCanceledException) when (!testCancelled.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No matching {method} within {WaitTimeout.TotalSeconds:0} s. Skipped: [{string.Join(", ", skipped)}]");
        }
    }

    public Task InvokeAsync(string method, params object?[] args) =>
        _connection.InvokeCoreAsync(method, args, TestContext.Current.CancellationToken);

    public Task<T> InvokeAsync<T>(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<T>(method, args, TestContext.Current.CancellationToken);

    /// <summary>Asserts the call fails with a <see cref="HubException"/> and returns the server's message.</summary>
    public async Task<string> ThrowsHubAsync(string method, params object?[] args)
    {
        var ex = await Assert.ThrowsAsync<HubException>(() => InvokeAsync(method, args));
        return ServerMessage(ex);
    }

    /// <summary>Returns <c>null</c> if the call succeeds, or the server's message if it fails with a <see cref="HubException"/>.</summary>
    public async Task<string?> TryInvokeMessageAsync(string method, params object?[] args)
    {
        try
        {
            await InvokeAsync(method, args);
            return null;
        }
        catch (HubException ex)
        {
            return ServerMessage(ex);
        }
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    // A predicate that can't be evaluated against a message's shape (missing property,
    // index out of range, wrong JSON kind) means "not this message", not a test failure.
    private static bool Matches(Func<JsonElement, bool>? match, JsonElement payload)
    {
        if (match is null) return true;
        try
        {
            return match(payload);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return false;
        }
    }

    // SignalR sends a HubException to the client as "An unexpected error occurred invoking
    // '<method>' on the server. HubException: <message>"; recover <message>.
    private static string ServerMessage(HubException ex)
    {
        const string marker = "HubException: ";
        var i = ex.Message.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? ex.Message : ex.Message[(i + marker.Length)..];
    }

    private sealed record Received(string Method, JsonElement[] Args)
    {
        public JsonElement Payload => Args.Length > 0 ? Args[^1] : default;
    }
}
