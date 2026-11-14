namespace Meridian.Logic.Tests.Turns;

public sealed class MoveTests
{
    [Fact]
    public void MoveTo_spends_the_path_cost()
    {
        var game = Play.Game().Put(Play.S1, Play.C(1), moves: 4);
        var events = game.Do(new MoveTo(Play.S1, Play.C(4)));
        Assert.Equal(Play.C(4), game.Crew(Play.S1).At);
        Assert.Equal(1, game.State.MovesLeft);
        Assert.Equal(new Moved(Play.S1, Play.C(1), Play.C(4), 3), Assert.Single(events));
    }

    [Fact]
    public void MoveTo_out_of_reach_is_rejected()
    {
        var game = Play.Game().Put(Play.S1, Play.C(1), moves: 2);
        Assert.DoesNotContain(new MoveTo(Play.S1, Play.C(4)), game.Legal(Play.S1));
        Assert.IsType<Rejected>(Assert.Single(game.Try(new MoveTo(Play.S1, Play.C(4))).Events));
    }

    [Fact]
    public void Moving_to_where_you_are_is_not_offered()
        => Assert.DoesNotContain(new MoveTo(Play.S1, Play.C(1)), Play.Game().Put(Play.S1, Play.C(1), 4).Legal(Play.S1));

    [Fact]
    public void Open_needs_a_movement_point()
    {
        var game = Play.Game().Put(Play.S1, Play.C(2), moves: 0);
        Assert.DoesNotContain(game.Legal(Play.S1), c => c is Open);
        game.State = game.State with { MovesLeft = 1 };
        Assert.Contains(new Open(Play.S1, Play.C(2), Side.E), game.Legal(Play.S1));
        Assert.Contains(new Open(Play.S1, Play.C(2), Side.W), game.Legal(Play.S1));
    }
}
