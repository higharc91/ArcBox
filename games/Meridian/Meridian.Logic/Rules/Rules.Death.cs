namespace Meridian.Logic;

// Death and echoes (spec §2.8; M3 plan, Task 2).
internal sealed partial class Resolver
{
    /// <summary>
    /// A crew member dies: their items drop where they stand, as tokens that keep the card, and they stay in the game as an
    /// echo. Anything they owed resolves as a timeout would have (M3 review focus 2). Their monsters keep acting on their
    /// turns (focus 3); scenarios that rule otherwise do so in their own <c>On(Died)</c>.
    /// </summary>
    internal void Kill(SeatId seat)
    {
        var crew = State.CrewOf(seat);
        if (!crew.Alive)
        {
            return;
        }
        var board = State.Board;
        foreach (var card in crew.Items)
        {
            board = board?.WithToken(new Token($"drop-{card.Value}", "item", crew.At, Audience.Everyone, Card: card));
            Events.Add(new ItemDropped(seat, card, crew.At));
        }
        State = State.WithCrew(crew with { Alive = false, Items = [] });
        if (board is not null)
        {
            State = State.WithBoard(board);
        }
        if (State.ActiveSeat == seat)
        {
            State = State with { MovesLeft = 0 };
        }
        Events.Add(new Died(seat));
        if (State.Waiting?.From == seat)
        {
            TimeoutNow();
        }
    }

    /// <summary>After the Breach a marker can reach 0, and a crew member with any marker at 0 is dead.</summary>
    private void CheckDeath(SeatId seat)
    {
        var crew = State.CrewOf(seat);
        if (State.Phase != Phase.Explore && crew.Alive && Traits.All.Any(t => crew.Steps[t] < 1))
        {
            Kill(seat);
        }
    }
}
