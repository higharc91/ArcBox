using System.Runtime.InteropServices;
using System.Text.Json;
using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>
/// REDLINE: a 22-car racing trivia game. Players claim seats in Driver Select, AI drives the rest,
/// then every round is Throttle → Question → Reveal until a car crosses the line (spec §2, §3).
/// </summary>
public sealed class RedlineGame(IQuestionSource questions, RedlineRules? rules = null, Grid? grid = null) : IGame
{
    public const string PhaseTimer = "phase";

    private readonly Grid _grid = grid ?? Grid2026.Load();

    private IGameContext? _ctx;
    private IReadOnlyList<Player> _players = [];
    private IReadOnlyList<TriviaQuestion> _questions = [];
    private string _attribution = HostViews.FallbackCredit;

    /// <summary>All 22 cars in team order; never reordered.</summary>
    private List<Driver> _field = [];
    /// <summary>All 22 cars in display order: team order in Driver Select, start order on the Grid, ranking afterwards.</summary>
    private List<Driver> _standings = [];
    private readonly Dictionary<PlayerId, Driver> _seatOf = new();
    private readonly Dictionary<PlayerId, bool> _connected = new();

    /// <summary>Driver Select: bumped whenever the free-seat set changes.</summary>
    private int _freeSeatsVersion;
    /// <summary>Driver Select: a "{CODE} was just taken" header, valid until the free-seat set next changes.</summary>
    private readonly Dictionary<PlayerId, string> _claimRejected = new();
    private readonly Dictionary<PlayerId, (PlayerViewKey Key, ControllerView View)> _views = new();

    public GameManifest Manifest { get; } = new(
        "redline",
        "REDLINE",
        "Racing trivia: bet your throttle on the category, then answer. 22-car 2026 grid.",
        2,
        8);

    public RedlineRules Rules { get; } = rules ?? new RedlineRules();

    public RacePhase Phase { get; private set; } = RacePhase.DriverSelect;

    /// <summary>The current round, 1..MaxRounds; 0 during Driver Select and Grid.</summary>
    public int Round { get; private set; }

    // ---------------------------------------------------------------- IGame

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        _ctx = ctx;
        _players = players;
        _questions = questions.Take(Rules.MaxRounds);
        _attribution = HostViews.Attribution(_questions);

        _field = [.. _grid.Seats.Select(s => new Driver(s))];
        _standings = [.. _field];
        _seatOf.Clear();
        _connected.Clear();
        _claimRejected.Clear();
        _views.Clear();
        _freeSeatsVersion = 0;
        foreach (var p in players) _connected[p.Id] = true;

        BeginDriverSelect();
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        if (_ctx is null || !_connected.ContainsKey(from)) return;
        try
        {
            switch (Phase)
            {
                case RacePhase.DriverSelect:
                    OnClaim(from, input.Read<ChoiceInput>());
                    break;
            }
        }
        catch (JsonException)
        {
            // Malformed payload: ignored (§3.3).
        }
    }

    public void OnTimer(string key)
    {
        if (_ctx is null || key != PhaseTimer) return;
        switch (Phase)
        {
            case RacePhase.DriverSelect: EndDriverSelect(); break;
            case RacePhase.Grid: StartRound(); break;
        }
    }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        if (_ctx is null || !_connected.ContainsKey(id)) return;
        _connected[id] = connected;
        if (_seatOf.TryGetValue(id, out var driver)) driver.Connected = connected;

        if (Phase == RacePhase.DriverSelect) MaybeEndDriverSelectEarly();
    }

    public object HostView() => HostViews.Build(
        Phase, Round, Rules, question: null, correctIndex: null, _standings,
        _ctx?.TimerDeadline(PhaseTimer), finish: null, _attribution);

    public ControllerView PlayerView(PlayerId id)
    {
        if (_ctx is null || !_connected.ContainsKey(id)) return PlayerViews.Idle();

        var key = ViewKey(id);
        if (_views.TryGetValue(id, out var cached) && cached.Key == key) return cached.View;

        var view = BuildPlayerView(id, key);
        _views[id] = (key, view);
        return view;
    }

    // ---------------------------------------------------------------- Driver Select

    private void BeginDriverSelect()
    {
        Phase = RacePhase.DriverSelect;
        Round = 0;
        for (var i = 0; i < _field.Count; i++) _field[i].Position = _field[i].PositionAtRoundStart = i + 1;
        _ctx!.SetTimer(PhaseTimer, Rules.DriverSelect);
    }

    private void OnClaim(PlayerId from, ChoiceInput choice)
    {
        if (_seatOf.ContainsKey(from) || choice.Selected is not [var code, ..]) return;

        var driver = _field.Find(d => d.Code == code);
        if (driver is null) return;                                   // unknown code
        if (driver.IsHuman)
        {
            _claimRejected[from] = PlayerViews.TakenHeader(code);     // lost a race for this seat
            return;
        }

        Seat(from, driver);
        MaybeEndDriverSelectEarly();
    }

    private void Seat(PlayerId id, Driver driver)
    {
        driver.PlayerId = id;
        driver.PlayerName = _players.First(p => p.Id == id).Name;
        driver.Connected = _connected[id];
        _seatOf[id] = driver;
        _freeSeatsVersion++;
        _claimRejected.Clear();
    }

    /// <summary>§3.1: ends once every connected player is seated. With nobody connected, the timer alone decides (§3.2).</summary>
    private void MaybeEndDriverSelectEarly()
    {
        var connected = _players.Where(p => _connected[p.Id]).ToList();
        if (connected.Count > 0 && connected.All(p => _seatOf.ContainsKey(p.Id))) EndDriverSelect();
    }

    private void EndDriverSelect()
    {
        var random = _ctx!.Random;

        foreach (var p in _players.Where(p => !_seatOf.ContainsKey(p.Id)))
        {
            var free = _field.Where(d => !d.IsHuman).ToList();
            Seat(p.Id, free[random.Next(free.Count)]);
        }
        // Every seat still without a player is now an AI driver (PlayerId stays null).

        _standings = [.. _field];
        random.Shuffle(CollectionsMarshal.AsSpan(_standings));
        for (var i = 0; i < _standings.Count; i++) _standings[i].Position = _standings[i].PositionAtRoundStart = i + 1;

        BeginGrid();
    }

    // ---------------------------------------------------------------- Grid

    private void BeginGrid()
    {
        Phase = RacePhase.Grid;
        _ctx!.SetTimer(PhaseTimer, Rules.Grid);
    }

    // ---------------------------------------------------------------- Rounds

    /// <summary>Lights out: round 1 begins. The round loop (Throttle → Question → Reveal) is added by Task 6.</summary>
    private void StartRound()
    {
        Round++;
        Phase = RacePhase.Throttle;
    }

    // ---------------------------------------------------------------- Phone views

    private PlayerViewKey ViewKey(PlayerId id)
    {
        var seated = _seatOf.TryGetValue(id, out var driver);
        return Phase == RacePhase.DriverSelect && !seated
            ? new PlayerViewKey(Phase, Round, null, _freeSeatsVersion, _claimRejected.GetValueOrDefault(id))
            : new PlayerViewKey(Phase, Round, driver?.Code, 0, null);
    }

    private ControllerView BuildPlayerView(PlayerId id, PlayerViewKey key)
    {
        var deadline = _ctx!.TimerDeadline(PhaseTimer);
        var driver = _seatOf.GetValueOrDefault(id);
        return (Phase, driver) switch
        {
            (RacePhase.DriverSelect, null) => PlayerViews.PickDriver(
                _field.Where(d => !d.IsHuman).Select(d => d.Seat), key.Header, deadline),
            (RacePhase.DriverSelect, _) => PlayerViews.Seated(driver.Seat, deadline),
            (RacePhase.Grid, not null) => PlayerViews.OnGrid(driver.Seat, deadline),
            _ => PlayerViews.Idle(),                                  // rounds: Task 6
        };
    }
}
