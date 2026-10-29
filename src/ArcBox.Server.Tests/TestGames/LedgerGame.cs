using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Server.Tests.TestGames;

public sealed record LedgerSeat(PlayerId Id, string Name, bool Connected, ProfileId? Profile);

/// <param name="CampaignId">The leased campaign, or null when started as a plain game.</param>
/// <param name="Version">The leased campaign's version as the game last saw it.</param>
public sealed record LedgerHostView(IReadOnlyList<LedgerSeat> Seats, IReadOnlyList<string> Log, string? CampaignId, long? Version);

/// <summary>
/// Records what the platform tells a game: every connection change as <c>connected:{id}:true|false</c> and every input as
/// <c>input:{id}</c>, and every host signal as <c>signal:{kind}:{payload}</c> (kind <c>boom</c> throws instead). The host view shows the seats and that log, so a test can wait for an entry. Input
/// <c>{"crash":true}</c> throws, and <c>{"save":"x"}</c> appends "x" to the campaign document's <c>notes</c> through the
/// lease (see <see cref="LedgerRules"/>).
/// </summary>
/// <remarks>
/// <c>ledger</c> uses campaigns and resumes after a restart: <see cref="Resume"/> logs <c>resume</c> and keeps the lease,
/// with every player disconnected. The same game registered with <see cref="Plain"/> (<c>plain-ledger</c>) is a game
/// without campaigns that isn't resumed (its manifest doesn't say so), for seat tests that don't need one.
/// </remarks>
public sealed class LedgerGame : IGame, ICampaignGame, IResumableGame, IHostAware
{
    public static readonly GameManifest Campaign = new("ledger", "Ledger", "Test game", 1, 8, UsesCampaigns: true,
        ResumesAfterRestart: true);
    public static readonly GameManifest Plain = new("plain-ledger", "Plain ledger", "Test game without campaigns", 1, 8);

    private readonly List<string> _log = [];
    private readonly Dictionary<PlayerId, bool> _connected = [];
    private IReadOnlyList<Player> _players = [];
    private ICampaignLease? _lease;

    public LedgerGame() : this(Campaign) { }

    public LedgerGame(GameManifest manifest) => Manifest = manifest;

    public GameManifest Manifest { get; }

    public void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign)
    {
        _lease = campaign;
        Start(ctx, players);
    }

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        _players = players;
        foreach (var p in players) _connected[p.Id] = true;
    }

    public void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign)
    {
        _lease = campaign;
        _players = players;
        foreach (var p in players) _connected[p.Id] = false;
        _log.Add("resume");
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        var payload = input.Payload;
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("crash", out var crash)
            && crash.ValueKind == JsonValueKind.True)
        {
            throw new InvalidOperationException("crash");
        }

        if (_lease is not null
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("save", out var save)
            && save.ValueKind == JsonValueKind.String)
        {
            var document = JsonNode.Parse(_lease.Document.GetRawText())!.AsObject();
            var notes = document["notes"] as JsonArray ?? [];
            notes.Add(save.GetString());
            document["notes"] = notes;
            _lease.Save(JsonSerializer.SerializeToElement(document));
        }

        _log.Add($"input:{from}");
    }

    public void OnHostSignal(string kind, PlayerInput payload)
    {
        if (kind == "boom")
        {
            throw new InvalidOperationException("boom");
        }

        _log.Add($"signal:{kind}:{payload.Payload.GetRawText()}");
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        _connected[id] = connected;
        _log.Add($"connected:{id}:{(connected ? "true" : "false")}");
    }

    public object HostView() => new LedgerHostView(
        [.. _players.Select(p => new LedgerSeat(p.Id, p.Name, _connected[p.Id], p.Profile))],
        [.. _log],
        _lease?.Info.Id,
        _lease?.Info.Version);

    public ControllerView PlayerView(PlayerId id) => new ButtonsView(null, [new Option("ping", "Ping")]);
}
