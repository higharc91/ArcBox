namespace Meridian.Logic.Tests.Turns;

public sealed class RejectionTests
{
    [Fact]
    public void A_command_from_a_seat_that_is_not_active_is_rejected_without_change()
    {
        // Review focus 7.
        var game = Play.Game();
        var before = game.State;
        var result = game.Try(new EndTurn(Play.S2));
        Assert.Same(before, result.State);
        Assert.Equal("It isn't your turn.", Assert.IsType<Rejected>(Assert.Single(result.Events)).Reason);
    }

    [Fact]
    public void An_answer_to_no_question_is_rejected()
    {
        var game = Play.Game();
        var result = game.Try(new Roll(Play.S1));
        Assert.IsType<Rejected>(Assert.Single(result.Events));
    }

    [Fact]
    public void A_seat_that_is_not_in_the_game_is_rejected()
        => Assert.IsType<Rejected>(Assert.Single(Play.Game().Try(new EndTurn(new SeatId("nobody"))).Events));

    [Fact]
    public void Nothing_is_legal_for_a_seat_that_is_not_active()
        => Assert.Empty(Play.Game().Legal(Play.S2));
}
