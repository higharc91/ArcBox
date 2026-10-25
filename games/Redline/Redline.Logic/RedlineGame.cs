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
    /// <summary>Per question (same index as <see cref="_questions"/>): its four answers in shuffled order and the correct one's index.</summary>
    private IReadOnlyList<(IReadOnlyList<string> Answers, int CorrectIndex)> _answers = [];
    private string _attribution = HostViews.FallbackCredit;

    /// <summary>All 22 cars in team order; never reordered.</summary>
    private List<Driver> _field = [];
    /// <summary>All 22 cars in display order: team order in Driver Select, start order on the Grid, ranking afterwards.</summary>
    private List<Driver> _standings = [];
    /// <summary>The classification behind <see cref="_standings"/>, with shared positions (§2.3).</summary>
    private IReadOnlyList<(Driver Driver, int Position)> _ranked = [];
    /// <summary>Chequered only: the awards (§2.4).</summary>
    private FinishSummary? _finish;
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
        _answers = [.. _questions.Select(q => ShuffleAnswers(q, ctx.Random))];
        _attribution = HostViews.Attribution(_questions);

        _field = [.. _grid.Seats.Select(s => new Driver(s))];
        _standings = [.. _field];
        _ranked = [];
        _finish = null;
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
                case RacePhase.Throttle:
                    OnThrottle(from, input.Read<ButtonsInput>());
                    break;
                case RacePhase.Question:
                    OnAnswer(from, input.Read<ChoiceInput>());
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
            case RacePhase.Throttle: EndThrottle(); break;
            case RacePhase.Question: Reveal(); break;
            case RacePhase.Reveal: EndReveal(); break;
            case RacePhase.Chequered: _ctx.EndGame(Ranking.Results(_ranked)); break;
        }
    }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        if (_ctx is null || !_connected.ContainsKey(id)) return;
        _connected[id] = connected;
        if (_seatOf.TryGetValue(id, out var driver)) driver.Connected = connected;

        switch (Phase)
        {
            case RacePhase.DriverSelect: MaybeEndDriverSelectEarly(); break;
            case RacePhase.Throttle: MaybeEndThrottleEarly(); break;
            case RacePhase.Question: MaybeEndQuestionEarly(); break;
        }
    }

    public object HostView() => HostViews.Build(
        Phase, Round, Rules, QuestionCard(), InRound ? _answers[Round - 1].CorrectIndex : null, _standings,
        _ctx?.TimerDeadline(PhaseTimer), _finish, _attribution);

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

    private static (IReadOnlyList<string> Answers, int CorrectIndex) ShuffleAnswers(TriviaQuestion q, Random random)
    {
        string[] answers = [q.Correct, .. q.Incorrect];
        random.Shuffle(answers);
        return (answers, Array.IndexOf(answers, q.Correct));
    }

    /// <summary>True in Throttle, Question and Reveal, the phases that have a current question.</summary>
    private bool InRound => Phase is RacePhase.Throttle or RacePhase.Question or RacePhase.Reveal;

    private TriviaQuestion Current => _questions[Round - 1];

    /// <summary>§4.4: category and difficulty in Throttle; the text and shuffled answers from Question on.</summary>
    private QuestionCard? QuestionCard() => Phase switch
    {
        RacePhase.Throttle => new QuestionCard(Current.Category, Current.Difficulty, null, null),
        RacePhase.Question or RacePhase.Reveal =>
            new QuestionCard(Current.Category, Current.Difficulty, Current.Text, _answers[Round - 1].Answers),
        _ => null,
    };

    private double AiAccuracy(Driver d) => AiPolicy.Accuracy(d.Seat.Driver.AiSkill, Current.Difficulty, Rules.Ai);

    /// <summary>
    /// Throttle phase. Resets the round state, forces pit-limited drivers to Cruise, and lets every other AI pick
    /// its throttle now (in team order) so AI never blocks early advance (§2.6).
    /// </summary>
    private void StartRound()
    {
        Round++;
        Phase = RacePhase.Throttle;
        foreach (var d in _field)
        {
            d.PositionAtRoundStart = d.Position;
            d.PreviousDistance = d.Distance;
            d.Throttle = d.PitLimiter ? Throttle.Cruise : null;     // a limited driver counts as locked (§3.2)
            d.AnswerIndex = null;
            d.Outcome = null;
        }

        var random = _ctx!.Random;
        var leader = _field.Max(d => d.Distance);
        var roundsLeft = Rules.MaxRounds - Round + 1;
        foreach (var d in _field.Where(d => !d.IsHuman && d.Throttle is null))
            d.Throttle = AiPolicy.ChooseThrottle(AiAccuracy(d), leader - d.Distance, roundsLeft, Rules.Ai, random);

        _ctx.SetTimer(PhaseTimer, Rules.Throttle);
        MaybeEndThrottleEarly();
    }

    private void OnThrottle(PlayerId from, ButtonsInput input)
    {
        if (!_seatOf.TryGetValue(from, out var driver) || driver.Throttle is not null) return;   // decided or pit-limited
        if (PlayerViews.ParseThrottle(input.Pressed) is not { } throttle) return;
        driver.Throttle = throttle;
        MaybeEndThrottleEarly();
    }

    private void MaybeEndThrottleEarly()
    {
        if (EveryConnectedHumanHas(d => d.Throttle is not null)) EndThrottle();
    }

    /// <summary>Question phase. Undecided drivers get Cruise; every AI answers now, in team order.</summary>
    private void EndThrottle()
    {
        foreach (var d in _field) d.Throttle ??= Throttle.Cruise;
        Phase = RacePhase.Question;

        var random = _ctx!.Random;
        var correctIndex = _answers[Round - 1].CorrectIndex;
        foreach (var d in _field.Where(d => !d.IsHuman))
            d.AnswerIndex = AiPolicy.Answer(AiAccuracy(d), correctIndex, random);

        _ctx.SetTimer(PhaseTimer, Rules.Question);
    }

    private void OnAnswer(PlayerId from, ChoiceInput input)
    {
        if (!_seatOf.TryGetValue(from, out var driver) || driver.AnswerIndex is not null) return;  // a limited driver still answers
        if (input.Selected is not [var id, ..] || PlayerViews.ParseAnswer(id) is not { } index) return;
        driver.AnswerIndex = index;
        MaybeEndQuestionEarly();
    }

    private void MaybeEndQuestionEarly()
    {
        if (EveryConnectedHumanHas(d => d.AnswerIndex is not null)) Reveal();
    }

    /// <summary>§3.2: AI never blocks. With no human connected, the phase runs on its timer alone.</summary>
    private bool EveryConnectedHumanHas(Func<Driver, bool> decided)
    {
        var connected = _seatOf.Values.Where(d => d.Connected).ToList();
        return connected.Count > 0 && connected.All(decided);
    }

    /// <summary>Reveal phase: scores all 22 cars (§2.2), sets next round's pit limiter, and re-ranks.</summary>
    private void Reveal()
    {
        Phase = RacePhase.Reveal;
        var correctIndex = _answers[Round - 1].CorrectIndex;
        foreach (var d in _field)
        {
            var throttle = d.Throttle ?? Throttle.Cruise;
            bool? correct = d.AnswerIndex is { } answer ? answer == correctIndex : null;
            var (distance, outcome, spinOut) = Scoring.Apply(d.Distance, throttle, correct, Rules);

            d.Distance = distance;
            d.Outcome = outcome;
            d.PitLimiter = spinOut;                                 // applies to the next round only
            if (correct == true) d.Correct++;
            if (throttle == Throttle.Redline) d.Redlines++;
            if (spinOut) d.SpinOuts++;
        }

        Rerank();
        _ctx!.SetTimer(PhaseTimer, Rules.Reveal);
    }

    /// <summary>
    /// §2.3 via <see cref="Ranking.Rank"/>: Distance desc, Correct desc, full ties sharing a position. Ranking the
    /// previous display order keeps full ties in the order they were already shown.
    /// </summary>
    private void Rerank()
    {
        _ranked = Ranking.Rank(_standings);
        _standings = [.. _ranked.Select(r => r.Driver)];
        foreach (var (driver, position) in _ranked) driver.Position = position;
    }

    /// <summary>After the Reveal timer: Chequered once any car has reached the line or the last round is done (§2.1), else the next round.</summary>
    private void EndReveal()
    {
        if (Round >= Rules.MaxRounds || _field.Any(d => d.Distance >= Rules.FinishLine)) BeginChequered();
        else StartRound();
    }

    // ---------------------------------------------------------------- Chequered

    /// <summary>The 10 s finish screen with the awards; its timer ends the game with the humans-only results.</summary>
    private void BeginChequered()
    {
        Phase = RacePhase.Chequered;
        _finish = new FinishSummary(Ranking.Awards(_ranked, Round));
        _ctx!.SetTimer(PhaseTimer, Rules.Chequered);
    }

    // ---------------------------------------------------------------- Phone views

    private PlayerViewKey ViewKey(PlayerId id)
    {
        var seated = _seatOf.TryGetValue(id, out var driver);
        if (Phase == RacePhase.DriverSelect && !seated)
            return new PlayerViewKey(Phase, Round, null, _freeSeatsVersion, _claimRejected.GetValueOrDefault(id));
        if ((InRound || Phase == RacePhase.Chequered) && driver is not null)
            return new PlayerViewKey(Phase, Round, driver.Code, 0, null,
                driver.Position, driver.Throttle, driver.AnswerIndex is not null, driver.PitLimiter,
                Phase == RacePhase.Reveal ? driver.Outcome : null);
        return new PlayerViewKey(Phase, Round, driver?.Code, 0, null);
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
            (RacePhase.Throttle or RacePhase.Question or RacePhase.Reveal, not null) => RoundView(driver, deadline),
            (RacePhase.Chequered, not null) => PlayerViews.Finished(
                driver.Position, _field.Count, PlayerViews.RoundHeader(driver.Position, Round, Rules.MaxRounds), deadline),
            _ => PlayerViews.Idle(),
        };
    }

    /// <summary>The §3.3 round views for one seated player.</summary>
    private ControllerView RoundView(Driver driver, DateTimeOffset? deadline)
    {
        var header = PlayerViews.RoundHeader(driver.Position, Round, Rules.MaxRounds);
        return Phase switch
        {
            RacePhase.Throttle when driver.PitLimiter => PlayerViews.PitLimited(header, deadline),
            RacePhase.Throttle when driver.Throttle is { } locked => PlayerViews.ThrottleLocked(locked, header, deadline),
            RacePhase.Throttle => PlayerViews.ChooseThrottle(Current, header, deadline),
            RacePhase.Question when driver.AnswerIndex is null => PlayerViews.Answer(
                Current.Text, _answers[Round - 1].Answers, driver.Throttle ?? Throttle.Cruise, header, deadline),
            RacePhase.Question => PlayerViews.Answered(header, deadline),
            _ => PlayerViews.Result(driver, Rules, header, deadline),
        };
    }
}
