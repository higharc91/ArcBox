using System.Runtime.InteropServices;
using System.Text.Json;
using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>
/// REDLINE: a 22-car racing trivia game. Players claim seats in Driver Select, AI drives the rest, qualifying
/// (QualifyingReading → QualifyingQuestion → QualifyingResult per lap) sets the grid (qualifying spec §2),
/// then every round is Throttle → Reading → Question → Reveal until a car crosses the line (spec §2, §3; Reading: audio spec §2).
/// After the Chequered flag, the top three players take the podium: Anthem, then Champagne (podium spec §2).
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
    /// <summary>
    /// Qualifying's easy questions, one per lap (qualifying spec §2.4; empty when off), and their answers, shuffled when
    /// qualifying begins.
    /// </summary>
    private IReadOnlyList<TriviaQuestion> _qualiQuestions = [];
    private IReadOnlyList<(IReadOnlyList<string> Answers, int CorrectIndex)> _qualiAnswers = [];
    private string _attribution = HostViews.FallbackCredit;

    /// <summary>All 22 cars in team order; never reordered.</summary>
    private List<Driver> _field = [];
    /// <summary>All 22 cars in display order: team order in Driver Select, start order on the Grid, ranking afterwards.</summary>
    private List<Driver> _standings = [];
    /// <summary>The classification behind <see cref="_standings"/>, with shared positions (§2.3).</summary>
    private IReadOnlyList<(Driver Driver, int Position)> _ranked = [];
    /// <summary>Chequered only: the awards (§2.4).</summary>
    private FinishSummary? _finish;
    /// <summary>Anthem and Champagne: the podium players, step 1 first, and the anthem's country.</summary>
    private List<PodiumPlace> _podium = [];
    private Country? _anthemCountry;
    private int _spraySeq;
    private readonly Dictionary<PlayerId, Driver> _seatOf = new();
    private readonly Dictionary<PlayerId, bool> _connected = new();

    /// <summary>The current qualifying lap, 1..QualifyingLaps; 0 before qualifying (and when it is skipped).</summary>
    private int _qualiLap;
    /// <summary>When the current QualifyingQuestion opened: the server's own lap clock starts here (§2.1).</summary>
    private DateTimeOffset _qualiOpenedAt;
    /// <summary>QualifyingResult: the code of the lap's fastest correct car (§2.2), or null.</summary>
    private string? _fastestLap;
    /// <summary>True once qualifying has set the grid, so the Grid phone view can say where the player qualified.</summary>
    private bool _qualified;

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
        if (_questions.Count < Rules.MaxRounds)
            throw new InvalidOperationException(
                $"The question source returned {_questions.Count} questions; REDLINE needs {Rules.MaxRounds}, one per round.");
        _answers = [.. _questions.Select(q => ShuffleAnswers(q, ctx.Random))];
        // Qualifying spec §2.4: taken after the race's, so the race keeps its questions with qualifying on or off. Their
        // answers are shuffled only when qualifying begins, after the seeded start order is drawn (BeginQualifying).
        _qualiQuestions = Rules.Qualify ? questions.Take(Rules.QualifyingLaps, Difficulty.Easy) : [];
        if (_qualiQuestions.Count < (Rules.Qualify ? Rules.QualifyingLaps : 0))
            throw new InvalidOperationException(
                $"The question source returned {_qualiQuestions.Count} easy questions; qualifying needs {Rules.QualifyingLaps}, one per lap.");
        _qualiAnswers = [];
        _attribution = HostViews.Attribution([.. _questions, .. _qualiQuestions]);

        _field = [.. _grid.Seats.Select(s => new Driver(s))];
        _standings = [.. _field];
        _ranked = [];
        _finish = null;
        _podium = [];
        _anthemCountry = null;
        _spraySeq = 0;
        _seatOf.Clear();
        _connected.Clear();
        _claimRejected.Clear();
        _views.Clear();
        _freeSeatsVersion = 0;
        _qualiLap = 0;
        _qualiOpenedAt = default;
        _fastestLap = null;
        _qualified = false;
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
                case RacePhase.QualifyingQuestion:
                    OnLapAnswer(from, input.Read<ChoiceInput>());
                    break;
                case RacePhase.Throttle:
                    OnThrottle(from, input.Read<ButtonsInput>());
                    break;
                case RacePhase.Question:
                    OnAnswer(from, input.Read<ChoiceInput>());
                    break;
                case RacePhase.Anthem or RacePhase.Champagne:
                    OnShake(from, input.Read<ShakeInput>());
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
            case RacePhase.QualifyingReading: OpenLap(); break;
            case RacePhase.QualifyingQuestion: LapResult(); break;
            case RacePhase.QualifyingResult: EndLapResult(); break;
            case RacePhase.Grid: StartRound(); break;
            case RacePhase.Throttle: EndThrottle(); break;
            case RacePhase.Reading: OpenQuestion(); break;
            case RacePhase.Question: Reveal(); break;
            case RacePhase.Reveal: EndReveal(); break;
            case RacePhase.Chequered: EndChequered(); break;
            case RacePhase.Anthem: BeginChampagne(); break;
            case RacePhase.Champagne: EndGame(); break;
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
            case RacePhase.QualifyingQuestion: MaybeEndLapEarly(); break;
            case RacePhase.Throttle: MaybeEndThrottleEarly(); break;
            case RacePhase.Question: MaybeEndQuestionEarly(); break;
        }
    }

    public object HostView() => HostViews.Build(
        Phase, Round, Rules, QuestionCard(), CurrentAnswers?.CorrectIndex, _standings,
        _ctx?.TimerDeadline(PhaseTimer), _finish, _attribution, PodiumHostView(), (_qualiLap, _fastestLap));

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

        // The seeded start order: the grid when qualifying is skipped, else qualifying's tie order (qualifying spec §2.2).
        // Every random draw up to here is the same with qualifying on, off or skipped, so this is today's shuffle.
        _standings = [.. _field];
        random.Shuffle(CollectionsMarshal.AsSpan(_standings));
        for (var i = 0; i < _standings.Count; i++)
            _standings[i].Position = _standings[i].PositionAtRoundStart = _standings[i].QualiTieOrder = i + 1;

        if (Rules.Qualify && _field.Any(d => d.IsHuman)) BeginQualifying();
        else BeginGrid();
    }

    // ---------------------------------------------------------------- Qualifying (qualifying spec §2)
    //
    // During qualifying _standings is the classification (the seeded order before lap 1's result), Position is the
    // classification position and PositionAtRoundStart the previous one, so a Result's position change is their difference.

    private TriviaQuestion CurrentLap => _qualiQuestions[_qualiLap - 1];

    /// <summary>Shuffles every lap's answers (after the seeded start order, so that order is today's shuffle), then lap 1.</summary>
    private void BeginQualifying()
    {
        _qualiAnswers = [.. _qualiQuestions.Select(q => ShuffleAnswers(q, _ctx!.Random))];
        BeginLap();
    }

    /// <summary>The next lap: QualifyingReading while the TV reads the question, or straight to the question with ReadQuestions off.</summary>
    private void BeginLap()
    {
        _qualiLap++;
        _fastestLap = null;
        foreach (var d in _field) d.PendingLapMs = d.PendingAnswer = null;
        if (!Rules.ReadQuestions)
        {
            OpenLap();
            return;
        }

        Phase = RacePhase.QualifyingReading;
        _ctx!.SetTimer(PhaseTimer, Rules.QualifyingReadingTime(CurrentLap.Text, _qualiLap));
    }

    /// <summary>
    /// QualifyingQuestion: the server's lap clock starts, and every AI's lap is drawn now, in team order, hidden until the
    /// Result like everyone else's (§2.3). An AI that runs out of time has no answer.
    /// </summary>
    private void OpenLap()
    {
        Phase = RacePhase.QualifyingQuestion;
        _qualiOpenedAt = _ctx!.Now;

        var random = _ctx.Random;
        var correctIndex = _qualiAnswers[_qualiLap - 1].CorrectIndex;
        foreach (var d in _field.Where(d => !d.IsHuman))
        {
            var (correct, ms) = Qualifying.AiLap(d.Seat.Driver.AiSkill, correctIndex, Rules, random);
            d.PendingLapMs = ms;
            d.PendingAnswer = ms is null ? null : correct ? correctIndex : (correctIndex + 1) % 4;
        }

        _ctx.SetTimer(PhaseTimer, Rules.QualifyingAnswer);
    }

    /// <summary>
    /// §2.1: a player's first parseable answer counts, timed as the phone's elapsedMs clamped against the server's own
    /// measurement since the question opened. Later inputs are ignored; so is anything after the window, which arrives in
    /// another phase.
    /// </summary>
    private void OnLapAnswer(PlayerId from, ChoiceInput input)
    {
        if (!_seatOf.TryGetValue(from, out var driver) || driver.PendingAnswer is not null) return;
        if (input.Selected is not [var id, ..] || PlayerViews.ParseAnswer(id) is not { } index) return;

        var serverMs = (int)(_ctx!.Now - _qualiOpenedAt).TotalMilliseconds;
        driver.PendingAnswer = index;
        driver.PendingLapMs = Qualifying.LapTimeMs(input.ElapsedMs, serverMs);
        MaybeEndLapEarly();
    }

    private void MaybeEndLapEarly()
    {
        if (EveryConnectedHumanHas(d => d.PendingAnswer is not null)) LapResult();
    }

    /// <summary>
    /// QualifyingResult: commits every car's lap (a time only for a correct answer; wrong or none is deleted), then
    /// classifies the field (§2.2) and finds the lap's fastest car.
    /// </summary>
    private void LapResult()
    {
        Phase = RacePhase.QualifyingResult;
        var correctIndex = _qualiAnswers[_qualiLap - 1].CorrectIndex;
        foreach (var d in _field)
        {
            int? lap = d.PendingAnswer == correctIndex ? d.PendingLapMs : null;
            d.QualiLaps.Add(lap);
            if (lap is not { } ms) continue;
            d.QualiCorrect++;
            d.QualiTotalMs += ms;
        }

        var classified = Qualifying.Classify(_field);
        for (var i = 0; i < classified.Count; i++)
        {
            classified[i].PositionAtRoundStart = classified[i].Position;
            classified[i].Position = i + 1;
        }
        _standings = [.. classified];
        _fastestLap = Qualifying.FastestLap(classified);

        _ctx!.SetTimer(PhaseTimer, Rules.QualifyingResult);
    }

    /// <summary>After a Result: the next lap, or after the last the classification becomes the grid (§2.2).</summary>
    private void EndLapResult()
    {
        if (_qualiLap < Rules.QualifyingLaps)
        {
            BeginLap();
            return;
        }

        foreach (var d in _standings) d.PositionAtRoundStart = d.Position;
        _qualified = true;
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

    /// <summary>True in Throttle, Reading, Question and Reveal, the phases that have a current question.</summary>
    private bool InRound => Phase is RacePhase.Throttle or RacePhase.Reading or RacePhase.Question or RacePhase.Reveal;

    /// <summary>The current race question's or qualifying lap's shuffled answers; null in the phases without a question.</summary>
    private (IReadOnlyList<string> Answers, int CorrectIndex)? CurrentAnswers =>
        InRound ? _answers[Round - 1] : HostViews.IsQualifying(Phase) ? _qualiAnswers[_qualiLap - 1] : null;

    private TriviaQuestion Current => _questions[Round - 1];

    /// <summary>
    /// §4.4: category and difficulty in Throttle; the text and shuffled answers from Reading on. Qualifying (spec §5): the
    /// whole card in all three phases, as in Reading.
    /// </summary>
    private QuestionCard? QuestionCard() => Phase switch
    {
        RacePhase.Throttle => new QuestionCard(Current.Category, Current.Difficulty, null, null),
        RacePhase.Reading or RacePhase.Question or RacePhase.Reveal =>
            new QuestionCard(Current.Category, Current.Difficulty, Current.Text, _answers[Round - 1].Answers),
        RacePhase.QualifyingReading or RacePhase.QualifyingQuestion or RacePhase.QualifyingResult =>
            new QuestionCard(CurrentLap.Category, CurrentLap.Difficulty, CurrentLap.Text, _qualiAnswers[_qualiLap - 1].Answers),
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
            d.Throttle = AiPolicy.ChooseThrottle(AiAccuracy(d), leader - d.Distance, roundsLeft, Rules, random);

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

    /// <summary>
    /// Undecided drivers get Cruise. Then the Reading phase (audio spec §2): the question is shown and read out, input
    /// is ignored, and only its timer ends it. With <see cref="RedlineRules.ReadQuestions"/> off, straight to Question.
    /// </summary>
    private void EndThrottle()
    {
        foreach (var d in _field) d.Throttle ??= Throttle.Cruise;
        if (!Rules.ReadQuestions)
        {
            OpenQuestion();
            return;
        }

        Phase = RacePhase.Reading;
        _ctx!.SetTimer(PhaseTimer, Rules.ReadingTime(Current.Text));
    }

    /// <summary>Question phase: the answer clock starts; every AI answers now, in team order.</summary>
    private void OpenQuestion()
    {
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

    /// <summary>After the Chequered flag: the podium ceremony, unless it is switched off or there are no players to stand on it.</summary>
    private void EndChequered()
    {
        var podium = Celebration.PodiumDrivers(_ranked);
        if (!Rules.Celebrate || podium.Count == 0)
        {
            EndGame();
            return;
        }

        _podium = [.. podium.Select((d, i) => new PodiumPlace(i + 1, d))];
        _anthemCountry = Countries.For(podium[0].Seat.Driver.Nationality);
        Phase = RacePhase.Anthem;
        _ctx!.SetTimer(PhaseTimer, Rules.Anthem);
    }

    // ---------------------------------------------------------------- Podium (podium spec §2)

    private void BeginChampagne()
    {
        foreach (var place in _podium) place.ChargeAtChampagne = Celebration.Quantise(place.Charge);
        Phase = RacePhase.Champagne;
        _ctx!.SetTimer(PhaseTimer, Rules.Champagne);
    }

    private void EndGame() => _ctx!.EndGame(Ranking.Results(_ranked));

    /// <summary>
    /// Anthem and Champagne: a valid charge (finite; clamped to 0..1) replaces the stored one. In Champagne only, a fire
    /// records a spray at the stored charge and empties the bottle, at most once per <see cref="RedlineRules.SprayCooldown"/>.
    /// </summary>
    private void OnShake(PlayerId from, ShakeInput input)
    {
        var place = _podium.Find(p => p.Player == from);
        if (place is null || !double.IsFinite(input.Charge)) return;
        place.Charge = Math.Clamp(input.Charge, 0, 1);

        if (!input.Fire || Phase != RacePhase.Champagne) return;
        var now = _ctx!.Now;
        if (place.LastSprayAt is { } last && now - last < Rules.SprayCooldown) return;
        place.Sprays.Add(new Spray(++_spraySeq, Celebration.Strength(place.Charge)));
        place.LastSprayAt = now;
        place.Charge = 0;
    }

    private Podium? PodiumHostView() =>
        Phase is RacePhase.Anthem or RacePhase.Champagne && _anthemCountry is { } country
            ? new Podium(country.Name, country.AnthemId, [.. _podium.Select(p => p.ToView())])
            : null;

    // ---------------------------------------------------------------- Phone views

    private PlayerViewKey ViewKey(PlayerId id)
    {
        var seated = _seatOf.TryGetValue(id, out var driver);
        if (Phase == RacePhase.DriverSelect && !seated)
            return new PlayerViewKey(Phase, Round, null, _freeSeatsVersion, _claimRejected.GetValueOrDefault(id));
        if (HostViews.IsQualifying(Phase) && driver is not null)
            return new PlayerViewKey(Phase, Round, driver.Code, 0, null,
                Position: driver.Position, Answered: driver.PendingAnswer is not null, QualiLap: _qualiLap,
                LapMs: Phase switch
                {
                    RacePhase.QualifyingQuestion => driver.PendingLapMs,
                    RacePhase.QualifyingResult => driver.QualiLaps[^1],
                    _ => null,
                });
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
            (RacePhase.QualifyingReading or RacePhase.QualifyingQuestion or RacePhase.QualifyingResult, not null) =>
                LapView(driver, deadline),
            (RacePhase.Grid, not null) => PlayerViews.OnGrid(driver.Seat, _qualified ? driver.Position : null, deadline),
            (RacePhase.Throttle or RacePhase.Reading or RacePhase.Question or RacePhase.Reveal, not null) => RoundView(driver, deadline),
            (RacePhase.Chequered, not null) => PlayerViews.Finished(
                driver.Position, _field.Count, PlayerViews.RoundHeader(driver.Position, Round, Rules.MaxRounds), deadline),
            (RacePhase.Anthem or RacePhase.Champagne, not null) => PodiumView(driver, deadline),
            _ => PlayerViews.Idle(),
        };
    }

    /// <summary>
    /// Podium spec §2: a podium player shakes (and in Champagne fires) their bottle; everyone else watches. The view does not
    /// depend on the charge, so it stays the same while the player shakes.
    /// </summary>
    private ControllerView PodiumView(Driver driver, DateTimeOffset? deadline)
    {
        var header = PlayerViews.RoundHeader(driver.Position, Round, Rules.MaxRounds);
        var place = _podium.Find(p => p.Driver == driver);
        if (place is null) return PlayerViews.WatchPodium(driver.Position, header, deadline);
        return Phase == RacePhase.Anthem
            ? PlayerViews.BuildPressure(driver.Seat.Team, header, deadline)
            : PlayerViews.PopTheCork(driver.Seat.Team, place.ChargeAtChampagne, header, deadline);
    }

    /// <summary>Qualifying spec §4.2: the lap views for one seated player.</summary>
    private ControllerView LapView(Driver driver, DateTimeOffset? deadline)
    {
        var header = PlayerViews.QualifyingHeader(_qualiLap, Rules.QualifyingLaps);
        return Phase switch
        {
            RacePhase.QualifyingReading => PlayerViews.QualifyingListening(CurrentLap.Text, header, deadline),
            RacePhase.QualifyingQuestion when driver.PendingLapMs is { } ms && driver.PendingAnswer is not null =>
                PlayerViews.LapChecking(ms, header, deadline),
            RacePhase.QualifyingQuestion => PlayerViews.Answer(CurrentLap.Text, _qualiAnswers[_qualiLap - 1].Answers, header, deadline),
            _ => PlayerViews.LapResult(driver.QualiLaps[^1], driver.Position, driver.Code == _fastestLap, header, deadline),
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
            RacePhase.Throttle => PlayerViews.ChooseThrottle(Current, Rules, header, deadline),
            RacePhase.Reading => PlayerViews.Listening(Current.Text, PlayerViews.WithThrottle(driver.Throttle, header), deadline),
            RacePhase.Question when driver.AnswerIndex is null => PlayerViews.Answer(
                Current.Text, _answers[Round - 1].Answers, PlayerViews.WithThrottle(driver.Throttle, header), deadline),
            RacePhase.Question => PlayerViews.Answered(header, deadline),
            _ => PlayerViews.Result(driver, Rules, header, deadline),
        };
    }
}
