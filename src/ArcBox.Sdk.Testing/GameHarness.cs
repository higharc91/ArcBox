using System.Text.Json;
using ArcBox.Sdk.Campaigns;

namespace ArcBox.Sdk.Testing;

public sealed class GameHarness<TGame> where TGame : IGame
{
    private readonly Dictionary<string, DateTimeOffset> _timers = new();
    private readonly HarnessGameContext _context;

    /// <param name="profiles">The profile of each player by index (null entries allowed); omitted, no player has one.</param>
    public GameHarness(TGame game, int playerCount = 4, int seed = 1, IReadOnlyList<ProfileId?>? profiles = null)
    {
        if (profiles is not null && profiles.Count > playerCount)
        {
            throw new ArgumentException($"{profiles.Count} profiles for {playerCount} players.", nameof(profiles));
        }

        Game = game;
        Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var players = new List<Player>(playerCount);
        for (int i = 1; i <= playerCount; i++)
        {
            players.Add(new Player(new PlayerId($"p{i}"), $"Player{i}", IsVip: i == 1, Profile: profiles is not null && i <= profiles.Count ? profiles[i - 1] : null));
        }
        Players = players;
        _context = new HarnessGameContext(this, seed);
    }

    public TGame Game { get; }
    public IReadOnlyList<Player> Players { get; }
    public DateTimeOffset Now { get; private set; }
    public GameResults? Results { get; private set; }
    public bool IsOver => Results != null;
    public IReadOnlyCollection<string> ActiveTimers => _timers.Keys.ToList();

    public void Start()
    {
        Game.Start(_context, Players);
    }

    /// <summary>Starts a campaign game with the lease, as the room does instead of <see cref="Start"/>.</summary>
    public void StartCampaign(ICampaignLease lease)
    {
        if (Game is not ICampaignGame g)
        {
            throw new InvalidOperationException($"{typeof(TGame).Name} does not implement ICampaignGame.");
        }

        g.Start(_context, Players, lease);
    }

    /// <summary>Resumes a game after a restart, as the room does instead of <see cref="Start"/>.</summary>
    public void Resume(ICampaignLease? lease)
    {
        if (Game is not IResumableGame g)
        {
            throw new InvalidOperationException($"{typeof(TGame).Name} does not implement IResumableGame.");
        }

        g.Resume(_context, Players, lease);
    }

    /// <summary>Delivers a signal from the TV to the game.</summary>
    public void HostSignal(string kind, object payload)
    {
        if (Game is not IHostAware g)
        {
            throw new InvalidOperationException($"{typeof(TGame).Name} does not implement IHostAware.");
        }

        g.OnHostSignal(kind, new PlayerInput(JsonSerializer.SerializeToElement(payload, ArcJson.Options)));
    }

    public void Input(int playerIndex, object payload)
    {
        if (IsOver)
        {
            return;
        }

        if (playerIndex < 0 || playerIndex >= Players.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        var jsonElement = JsonSerializer.SerializeToElement(payload, ArcJson.Options);
        Game.OnInput(Players[playerIndex].Id, new PlayerInput(jsonElement));
    }

    public void Advance(TimeSpan by)
    {
        if (IsOver)
        {
            return;
        }

        var target = Now + by;

        while (!IsOver)
        {
            var due = _timers
                .Where(kvp => kvp.Value <= target)
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => (KeyValuePair<string, DateTimeOffset>?)kvp)
                .FirstOrDefault();

            if (due == null)
            {
                break;
            }

            var timer = due.Value;
            Now = timer.Value;
            _timers.Remove(timer.Key);
            Game.OnTimer(timer.Key);
        }

        Now = target;
    }

    public void SetConnected(int playerIndex, bool connected)
    {
        if (playerIndex < 0 || playerIndex >= Players.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        Game.OnPlayerConnectionChanged(Players[playerIndex].Id, connected);
    }

    public THostView HostView<THostView>()
    {
        return (THostView)Game.HostView();
    }

    public ControllerView PlayerView(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Players.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(playerIndex));
        }

        return Game.PlayerView(Players[playerIndex].Id);
    }

    private sealed class HarnessGameContext : IGameContext
    {
        private readonly GameHarness<TGame> _harness;

        public HarnessGameContext(GameHarness<TGame> harness, int seed)
        {
            _harness = harness;
            Random = new Random(seed);
        }

        public DateTimeOffset Now => _harness.Now;
        public Random Random { get; }

        public void SetTimer(string key, TimeSpan after)
        {
            _harness._timers[key] = _harness.Now + after;
        }

        public void CancelTimer(string key)
        {
            _harness._timers.Remove(key);
        }

        public DateTimeOffset? TimerDeadline(string key)
        {
            return _harness._timers.TryGetValue(key, out var deadline) ? deadline : null;
        }

        public void EndGame(GameResults results)
        {
            _harness.Results = results;
        }
    }
}
