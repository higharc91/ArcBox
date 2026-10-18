using System.Text.Json;

namespace ArcBox.Sdk.Testing;

public sealed class GameHarness<TGame> where TGame : IGame
{
    private readonly Dictionary<string, DateTimeOffset> _timers = new();
    private readonly HarnessGameContext _context;

    public GameHarness(TGame game, int playerCount = 4, int seed = 1)
    {
        Game = game;
        Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var players = new List<Player>(playerCount);
        for (int i = 1; i <= playerCount; i++)
        {
            players.Add(new Player(new PlayerId($"p{i}"), $"Player{i}", IsVip: i == 1));
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
