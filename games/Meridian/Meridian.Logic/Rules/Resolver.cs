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
            case EndTurn: EndTurnNow(); break;
            case Timeout: TimeoutNow(); break;
            default: throw new InvalidOperationException($"No handler for {command.GetType().Name}.");
        }
    }

    /// <summary>The legal answers to <paramref name="waiting"/>, not counting Timeout.</summary>
    public static void Answers(RulesPack pack, GameState state, Pending waiting, List<Command> legal)
    {
        _ = (pack, state, waiting, legal);
    }

    /// <summary>The active seat's free actions, not counting EndTurn and Timeout.</summary>
    public static void Actions(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        _ = (pack, state, seat, legal);
    }

    /// <summary>Timeout resolves whatever the seat owes (plan, Task 4): with nothing owed, it ends the turn.</summary>
    private void TimeoutNow()
    {
        switch (State.Waiting)
        {
            case null:
                EndTurnNow();
                break;
        }
    }

    private void EndTurnNow()
    {
        var ending = State.ActiveSeat;
        Events.Add(new TurnEnded(ending));
        var next = (State.Active + 1) % State.Crew.Count;
        var round = next == State.First ? State.Round + 1 : State.Round;
        var moves = Rules.Value(Pack, State.Crew[next], Trait.Agility);
        State = State with { Active = next, Round = round, MovesLeft = moves, Turn = new TurnFlags(), Waiting = null };
        Events.Add(new TurnStarted(State.ActiveSeat, round, moves));
    }
}
