using ArcBox.Sdk;

namespace ArcBox.Server.Tests.TestGames;

public sealed record LedgerSeat(PlayerId Id, string Name, bool Connected, ProfileId? Profile);

public sealed record LedgerHostView(IReadOnlyList<LedgerSeat> Seats, IReadOnlyList<string> Log);

/// <summary>
/// Records what the platform tells a game: every connection change as <c>connected:{id}:true|false</c> and every input as
/// <c>input:{id}</c>. The host view shows the seats and that log, so a test can wait for an entry. Input
/// <c>{"crash":true}</c> throws.
/// </summary>
public sealed class LedgerGame : IGame
{
    private readonly List<string> _log = [];
    private readonly Dictionary<PlayerId, bool> _connected = [];
    private IReadOnlyList<Player> _players = [];

    public GameManifest Manifest { get; } = new("ledger", "Ledger", "Test game", 1, 8);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        _players = players;
        foreach (var p in players) _connected[p.Id] = true;
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        if (input.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && input.Payload.TryGetProperty("crash", out var crash)
            && crash.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            throw new InvalidOperationException("crash");
        }

        _log.Add($"input:{from}");
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        _connected[id] = connected;
        _log.Add($"connected:{id}:{(connected ? "true" : "false")}");
    }

    public object HostView() => new LedgerHostView(
        [.. _players.Select(p => new LedgerSeat(p.Id, p.Name, _connected[p.Id], p.Profile))],
        [.. _log]);

    public ControllerView PlayerView(PlayerId id) => new ButtonsView(null, [new Option("ping", "Ping")]);
}
