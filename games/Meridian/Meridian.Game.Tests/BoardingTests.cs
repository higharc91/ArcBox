namespace Meridian.Game.Tests;

public sealed class BoardingTests
{
    [Fact]
    public void Manifest_seats_three_to_six()
    {
        var manifest = new Harness().Game.Manifest;
        Assert.Equal(("meridian", 3, 6), (manifest.Id, manifest.MinPlayers, manifest.MaxPlayers));
        Assert.False(manifest.UsesCampaigns);
    }

    [Fact]
    public void Everyone_picks_from_the_crew_roster()
    {
        var game = new Harness();
        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal("crewPick", game.Screen(i)));
        var screen = game.Data<CrewPickScreen>(0);
        Assert.Equal(8, screen.Crew.Count);
        Assert.Equal(4, screen.Crew[0].Tracks.Count);
        Assert.Equal(game.H.Now.AddSeconds(45), game.H.PlayerView(0).DeadlineUtc);
    }

    [Fact]
    public void A_taken_crew_member_is_rejected()
    {
        var game = new Harness();
        var first = game.Pack.Crew[0].Id.Value;
        game.Send(0, "pick", new { crew = first });
        game.Send(1, "pick", new { crew = first });
        Assert.Null(game.Session.Seats[1].Crew);
        Assert.True(game.Data<CrewPickScreen>(1).Crew[0].Taken);
        Assert.True(game.Data<CrewPickScreen>(0).Crew[0].Mine);
    }

    [Fact]
    public void A_player_can_change_their_pick()
    {
        var game = new Harness();
        game.Send(0, "pick", new { crew = game.Pack.Crew[0].Id.Value });
        game.Send(0, "pick", new { crew = game.Pack.Crew[1].Id.Value });
        Assert.Equal(game.Pack.Crew[1].Id, game.Session.Seats[0].Crew);
    }

    [Fact]
    public void The_last_pick_starts_the_game()
    {
        var game = new Harness().Board();
        Assert.Equal(SessionPhase.Explore, game.Session.Phase);
        Assert.Equal(Phase.Explore, game.State.Phase);
        Assert.Equal(game.Pack.Crew[2].Id, game.State.CrewOf(game.Seat(2)).Crew);
    }

    [Fact]
    public void Picking_doesnt_restart_the_boarding_clock()
    {
        var game = new Harness();
        game.Advance(30);
        game.Send(0, "pick", new { crew = game.Pack.Crew[0].Id.Value });
        Assert.Equal(game.H.Now.AddSeconds(15), game.H.PlayerView(1).DeadlineUtc);
    }

    [Fact]
    public void Boarding_timeout_assigns_free_crew_and_starts()
    {
        var game = new Harness();
        game.Send(1, "pick", new { crew = game.Pack.Crew[5].Id.Value });
        game.Advance(45);
        Assert.Equal(SessionPhase.Explore, game.Session.Phase);
        var crew = game.Session.Seats.Select(s => s.Crew).ToList();
        Assert.All(crew, c => Assert.NotNull(c));
        Assert.Equal(3, crew.Distinct().Count());
        Assert.Equal(game.Pack.Crew[5].Id, crew[1]);
    }

    [Fact]
    public void Seats_take_turns_in_join_order()
    {
        var game = new Harness(players: 4).Board();
        Assert.Equal(Enumerable.Range(0, 4).Select(game.Seat), game.State.Crew.Select(c => c.Seat));
    }

    [Fact]
    public void Garbage_input_is_ignored()
    {
        var game = new Harness();
        game.Send(0, "pick", new { crew = 7 });
        game.Send(0, "dance");
        game.H.Input(0, new { nonsense = true });
        Assert.All(game.Session.Seats, s => Assert.Null(s.Crew));
    }
}
