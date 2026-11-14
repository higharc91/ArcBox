namespace Meridian.Logic;

/// <summary>
/// Applies one legal command. Mutable, but it lives only inside one <see cref="Rules.Step"/> call, so <c>Step</c> stays
/// pure from the outside; the handlers just don't have to thread a state and an event list through every call.
/// The handlers are split across the <c>Rules.*.cs</c> files by topic.
/// </summary>
internal sealed partial class Resolver(RulesPack pack, GameState state)
{
    public RulesPack Pack { get; } = pack;
    public GameState State { get; private set; } = state;
    public List<GameEvent> Events { get; } = [];

    private MeridianRules R => Pack.Rules;

    public void Apply(Command command)
    {
        switch (command)
        {
            case EndTurn: FinishTurn(); break;
            case Timeout: TimeoutNow(); break;
            case MoveTo m: Move(m); break;
            case Open o: OpenHatch(o); break;
            case Orient o: OrientTile(o.Rotation); break;
            case Roll: RollDice(); break;
            case Choose c: ChooseOption(c.Option); break;
            case UseItem u: Use(u.Item); break;
            case UseCompartment: UseRoom(); break;
            case OfferTrade o: Offer(o); break;
            case AnswerTrade a: Answer(a.Accept); break;
            case Attack a: AttackTarget(a); break;
            case Allocate a: AllocateDamage(a.First); break;
            case MonsterMove m: MoveMonster(m); break;
            case MonsterAttack m: MonsterAttacks(m); break;
            case Lend l: LendDie(l); break;
            default: throw new InvalidOperationException($"No handler for {command.GetType().Name}.");
        }
        AfterStep();
    }

    /// <summary>The legal answers to <paramref name="waiting"/>, not counting Timeout.</summary>
    public static void Answers(RulesPack pack, GameState state, Pending waiting, List<Command> legal)
    {
        switch (waiting)
        {
            case OrientPending o:
                legal.AddRange(o.Rotations.Select(r => new Orient(o.From, r)));
                break;
            case RollPending r:
                legal.Add(new Roll(r.From));
                break;
            case ChoicePending c:
                legal.AddRange(pack.Card(c.Card).Choice!.Options.Select(o => new Choose(c.From, o.Id)));
                break;
            case TradePending t:
                legal.Add(new AnswerTrade(t.From, true));
                legal.Add(new AnswerTrade(t.From, false));
                break;
            case AllocatePending a:
                legal.AddRange(Enumerable.Range(0, a.Steps + 1).Select(k => new Allocate(a.From, k)));
                break;
        }
    }

    /// <summary>The active seat's free actions, not counting EndTurn and Timeout.</summary>
    public static void Actions(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        if (state.CrewOf(seat).Aboard)
        {
            LegalMoves(pack, state, seat, legal);
            LegalOpens(pack, state, seat, legal);
            LegalItems(pack, state, seat, legal);
            LegalRoom(pack, state, seat, legal);
        }
        LegalCombat(pack, state, seat, legal);
    }

    /// <summary>Timeout resolves whatever the seat owes (plan, Task 4): with nothing owed, it ends the turn.</summary>
    private void TimeoutNow()
    {
        switch (State.Waiting)
        {
            case null:
                FinishTurn();
                break;
            case OrientPending o:
                OrientTile(Placement.DefaultRotation(State.Ship, Pack.Tile(o.Tile), o.At, o.Toward, R.DeckRadius));
                break;
            case RollPending:
                RollDice();
                break;
            case ChoicePending c:
                ChooseOption(Pack.Card(c.Card).Choice!.Options[0].Id);
                break;
            case TradePending:
                Answer(false);
                break;
            case AllocatePending a:
                AllocateDamage(DefaultSplit(State, a));
                break;
        }
    }

    /// <summary>
    /// Passes the turn to the next seat still in the turn order (escaped seats leave it; the dead stay, as echoes). A new
    /// round starts on passing the seat that went first. The new seat's stunned monsters recover, missing this activation.
    /// </summary>
    private void FinishTurn()
    {
        var ending = State.ActiveSeat;
        Events.Add(new TurnEnded(ending));
        var next = State.Active;
        var round = State.Round;
        for (var i = 0; i < State.Crew.Count; i++)
        {
            next = (next + 1) % State.Crew.Count;
            if (next == State.First)
            {
                round++;
            }
            if (!State.Crew[next].Escaped)
            {
                break;
            }
        }
        var crew = State.Crew[next];
        var moves = crew.Aboard ? Rules.Value(Pack, crew, Trait.Agility) : 0;
        var turn = new TurnFlags();
        if (State.Board is { } board)
        {
            foreach (var monster in board.Tokens.Where(t => t.Controller == crew.Seat && t.Stunned))
            {
                board = board.WithToken(monster with { Stunned = false });
                turn = turn with
                {
                    MonstersMoved = turn.MonstersMoved.Add(monster.Id),
                    MonstersAttacked = turn.MonstersAttacked.Add(monster.Id),
                };
                Events.Add(new MonsterRecovered(monster.Id));
            }
            State = State.WithBoard(board);
        }
        var newRound = round != State.Round;
        State = State with { Active = next, Round = round, MovesLeft = moves, Turn = turn, Waiting = null };
        if (newRound)
        {
            RoundEnded(round - 1);
        }
        Events.Add(new TurnStarted(State.ActiveSeat, round, moves));
    }

    /// <summary>Round-end hazards and scenario rules (M3 plan, Task 4).</summary>
    partial void RoundEnded(int round);
}
