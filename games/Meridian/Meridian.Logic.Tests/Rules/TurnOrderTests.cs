namespace Meridian.Logic.Tests.Turns;

public sealed class TurnOrderTests
{
    [Fact]
    public void EndTurn_passes_to_the_next_seat_and_wraps_into_a_new_round()
    {
        var game = Play.Game();
        game.Do(new EndTurn(Play.S1));
        Assert.Equal(Play.S2, game.State.ActiveSeat);
        Assert.Equal(1, game.State.Round);
        game.Do(new EndTurn(Play.S2));
        game.Do(new EndTurn(Play.S3));
        Assert.Equal(Play.S1, game.State.ActiveSeat);
        Assert.Equal(2, game.State.Round);
    }

    [Fact]
    public void A_new_turn_resets_moves_and_flags()
    {
        var game = Play.Game();
        game.State = game.State with { Turn = new TurnFlags(UsedItem: true), MovesLeft = 0 };
        var events = game.Do(new EndTurn(Play.S1));
        Assert.Equal(new TurnFlags(), game.State.Turn);
        Assert.Equal(Rules.Value(game.Pack, game.Crew(Play.S2), Trait.Agility), game.State.MovesLeft);
        Assert.Equal([new TurnEnded(Play.S1), new TurnStarted(Play.S2, 1, game.State.MovesLeft)], events);
    }

    [Fact]
    public void Rounds_count_from_the_seat_that_went_first()
    {
        var game = Play.Game();
        game.State = game.State with { First = 1, Active = 1 };
        game.Do(new EndTurn(Play.S2));
        game.Do(new EndTurn(Play.S3));
        Assert.Equal(1, game.State.Round);
        game.Do(new EndTurn(Play.S1));
        Assert.Equal(2, game.State.Round);
    }

    [Fact]
    public void Timeout_with_nothing_owed_ends_the_turn()
    {
        var game = Play.Game();
        game.Do(new Timeout(Play.S1));
        Assert.Equal(Play.S2, game.State.ActiveSeat);
    }

    [Fact]
    public void EndTurn_is_always_legal_for_the_active_seat()
    {
        // Review focus 5: with nowhere to go and nothing to use, the turn can still end.
        var game = Play.Game();
        game.State = game.State with { MovesLeft = 0, Turn = new TurnFlags(true, true, true) };
        Assert.Contains(new EndTurn(Play.S1), game.Legal(Play.S1));
    }
}
