namespace Meridian.Game.Tests;

public sealed class DebriefTests
{
    private static Harness AtDebrief(string scenario = ThePassenger.Id)
    {
        var game = new Harness(4, 1, scenario).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Debrief);
        Assert.Equal(SessionPhase.Debrief, game.Session.Phase);
        return game;
    }

    [Fact]
    public void The_debrief_waits_for_the_vip()
    {
        var game = AtDebrief();
        Assert.Null(game.H.Results);
        game.Send(1, "continue");   // not the VIP
        Assert.Null(game.H.Results);
        game.Send(0, "continue");
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
    }

    [Fact]
    public void The_debrief_ends_by_itself_after_twenty_seconds()
    {
        var game = AtDebrief();
        game.Advance(20);
        Assert.NotNull(game.H.Results);
    }

    [Fact]
    public void Results_score_winners_one_and_list_them_first()
    {
        var game = AtDebrief();
        var winners = game.State.Board!.Outcome!.Winners;
        game.Send(0, "continue");
        var scores = game.H.Results!.Scores;
        Assert.Equal(winners.Count, scores.Count(s => s.Score == 1));
        Assert.True(scores.SkipWhile(s => s.Score == 1).All(s => s.Score == 0));
        Assert.Equal(game.State.Board.Outcome.Message, game.H.Results.Message);
    }

    [Fact]
    public void The_debrief_reveals_every_role()
    {
        var game = AtDebrief(OneOfUsIsnt.Id);
        var mimic = game.State.Board!.Roles.Single(r => r.Role == OneOfUsIsnt.Mimic).Seat;
        Assert.Equal(OneOfUsIsnt.Mimic, Visibility.For(game.State, null).RoleOf(mimic)!.Role);
    }

    [Fact]
    public void Awards_name_a_clear_winner_or_nobody()
    {
        var game = AtDebrief();
        foreach (var (title, seat) in game.Game.Awards())
        {
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.True(game.State.IndexOf(seat) >= 0);
        }
    }
}
