using System.Text.Json;
using ArcBox.Sdk;

namespace Counter.Logic;

public sealed class CounterGame : IGame
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(20);

    private IGameContext? _context;
    private IReadOnlyList<Player> _players = [];
    private readonly Dictionary<PlayerId, int> _counts = new();
    private ControllerView? _playerView;

    public GameManifest Manifest { get; } = new(
        "counter",
        "Counter",
        "Mash the button. Most presses in 20 seconds wins.",
        1,
        8
    );

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        _context = ctx;
        _players = players;
        _counts.Clear();
        foreach (var player in players)
        {
            _counts[player.Id] = 0;
        }

        ctx.SetTimer("end", Duration);

        _playerView = new ButtonsView("Mash it!", [new Option("press", "+1")])
        {
            DeadlineUtc = ctx.TimerDeadline("end")
        };
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        try
        {
            var buttons = input.Read<ButtonsInput>();
            if (buttons.Pressed == "press")
            {
                if (_counts.ContainsKey(from))
                {
                    _counts[from]++;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed payload ignored
        }
    }

    public void OnTimer(string key)
    {
        if (key == "end" && _context != null)
        {
            var scores = _players
                .Select(p => new PlayerScore(p.Id, p.Name, _counts.TryGetValue(p.Id, out var count) ? count : 0))
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .ToList();

            _context.EndGame(new GameResults(scores));
        }
    }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        // Players keep their count across disconnection
    }

    public object HostView()
    {
        var rows = _players
            .Select(p => new CounterRow(p.Name, _counts.TryGetValue(p.Id, out var count) ? count : 0))
            .ToList();

        return new CounterHostView(rows, _context?.TimerDeadline("end"));
    }

    public ControllerView PlayerView(PlayerId id)
    {
        return _playerView ?? new ButtonsView("Mash it!", [new Option("press", "+1")]);
    }
}
