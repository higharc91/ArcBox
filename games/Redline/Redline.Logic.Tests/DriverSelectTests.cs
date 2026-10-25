using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

public class DriverSelectTests
{
    [Fact]
    public void Manifest_and_take_contract()
    {
        var q = new FakeQuestionSource();
        var game = new RedlineGame(q);
        Assert.Equal(("redline", 2, 8), (game.Manifest.Id, game.Manifest.MinPlayers, game.Manifest.MaxPlayers));
        Assert.Equal(0, q.TakeCalls);
        var h = new GameHarness<RedlineGame>(game); h.Start();
        Assert.Equal(1, q.TakeCalls);
    }

    [Fact]
    public void Starts_in_driver_select_with_22_free_seats_and_hints()
    {
        var (h, _) = RedlineKit.Start();
        Assert.Equal(RacePhase.DriverSelect, RedlineKit.View(h).Phase);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(20), RedlineKit.View(h).DeadlineUtc);
        var v = Assert.IsType<ChoiceView>(h.PlayerView(0));
        Assert.Equal(22, v.Options.Count);
        Assert.Equal(new Option("NOR", "#1 Lando Norris", "McLaren · MCL40"), v.Options[0]);
    }

    [Fact]
    public void Claim_seats_player_and_removes_seat_for_others_only()
    {
        var (h, _) = RedlineKit.Start();
        var p2Before = h.PlayerView(2);
        RedlineKit.Claim(h, 0, "VER");
        Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.DoesNotContain(((ChoiceView)h.PlayerView(1)).Options, o => o.Id == "VER");
        Assert.NotSame(p2Before, h.PlayerView(2));
        Assert.Equal(new PlayerId("p1"), RedlineKit.View(h).Drivers.Single(d => d.Code == "VER").Player);
    }

    [Fact]
    public void Taken_seat_is_rejected_with_header()
    {
        var (h, _) = RedlineKit.Start();
        RedlineKit.Claim(h, 0, "VER");
        RedlineKit.Claim(h, 1, "VER");                          // raced: list was stale
        Assert.Equal("VER was just taken: pick another", h.PlayerView(1).Header);
        Assert.Equal(new PlayerId("p1"), RedlineKit.View(h).Drivers.Single(d => d.Code == "VER").Player);
    }

    [Fact]
    public void All_seated_advances_to_grid_and_ai_fills_the_rest()
    {
        var (h, _) = RedlineKit.Start(players: 2);
        RedlineKit.Claim(h, 0, "LEC"); RedlineKit.Claim(h, 1, "ALO");
        var v = RedlineKit.View(h);
        Assert.Equal(RacePhase.Grid, v.Phase);
        Assert.Equal(22, v.Drivers.Count);
        Assert.Equal(20, v.Drivers.Count(d => d.Player is null));
        Assert.Equal("You're Charles Leclerc, #16, Ferrari. Lights out in a moment…", ((WaitView)h.PlayerView(0)).Message);
    }

    [Fact]
    public void Timeout_seats_unseated_players_randomly()
    {
        var (h, _) = RedlineKit.Start(players: 3);
        RedlineKit.Claim(h, 0, "NOR");
        h.Advance(TimeSpan.FromSeconds(20));
        var v = RedlineKit.View(h);
        Assert.Equal(3, v.Drivers.Count(d => d.Player is not null));
        Assert.Equal("NOR", RedlineKit.Row(h, 0).Code);
    }

    [Fact]
    public void Attribution_credits_opentdb_when_any_question_came_from_it()
    {
        var (h, _) = RedlineKit.Start();                                  // FakeQuestionSource → Origin OpenTdb
        Assert.Equal("Questions: Open Trivia DB (CC BY-SA 4.0)", RedlineKit.View(h).Attribution);
    }

    // Review Focus 1
    [Fact]
    public void Seated_player_who_disconnects_keeps_their_driver()
    {
        var (h, _) = RedlineKit.Start(players: 2);
        RedlineKit.Claim(h, 0, "HAM"); h.SetConnected(0, false);
        RedlineKit.Claim(h, 1, "HAM");
        Assert.Equal("HAM was just taken: pick another", h.PlayerView(1).Header);
        h.SetConnected(0, true);
        Assert.Equal("HAM", RedlineKit.Row(h, 0).Code);
    }

    // Review Focus 2
    [Fact]
    public void Everyone_disconnected_still_starts_on_timeout()
    {
        var (h, _) = RedlineKit.Start();
        for (var i = 0; i < 4; i++) h.SetConnected(i, false);
        h.Advance(TimeSpan.FromSeconds(20 + 6));
        Assert.Equal(RacePhase.Throttle, RedlineKit.View(h).Phase);
        Assert.Equal(4, RedlineKit.View(h).Drivers.Count(d => d.Player is not null));
    }

    // Beyond the brief: view-instance stability and ignored input (§3.3 "Anything else is ignored").
    [Fact]
    public void Unchanged_view_is_the_same_instance_and_bad_input_is_ignored()
    {
        var (h, _) = RedlineKit.Start();
        var before = h.PlayerView(1);
        Assert.Same(before, h.PlayerView(1));
        RedlineKit.Claim(h, 1, "XXX");                                    // unknown code
        h.Input(1, new { nope = 1 });                                     // malformed payload
        h.Input(1, new { selected = Array.Empty<string>() });             // nothing selected
        Assert.Same(before, h.PlayerView(1));
        RedlineKit.Claim(h, 0, "PIA");
        var seated = h.PlayerView(0);
        RedlineKit.Claim(h, 0, "NOR");                                    // already seated
        Assert.Same(seated, h.PlayerView(0));
        Assert.Equal("PIA", RedlineKit.Row(h, 0).Code);
        Assert.Null(RedlineKit.View(h).Drivers.Single(d => d.Code == "NOR").Player);
    }

    [Fact]
    public void Driver_select_rows_follow_team_order_and_grid_rows_follow_start_order()
    {
        var (h, _) = RedlineKit.Start(players: 2);
        var select = RedlineKit.View(h);
        Assert.Equal(0, select.Round);
        Assert.Null(select.Question);
        Assert.Equal(["NOR", "PIA", "LEC"], select.Drivers.Take(3).Select(d => d.Code));
        Assert.Equal(Enumerable.Range(1, 22), select.Drivers.Select(d => d.Position));
        Assert.All(select.Drivers, d => Assert.Equal(DriverStatus.Unclaimed, d.Status));

        RedlineKit.Claim(h, 0, "LEC");
        Assert.Equal(DriverStatus.Seated, RedlineKit.Row(h, 0).Status);
        Assert.Equal("You're Charles Leclerc. Waiting for the grid…", ((WaitView)h.PlayerView(0)).Message);
        RedlineKit.Claim(h, 1, "ALO");

        var grid = RedlineKit.View(h);
        Assert.Equal(RacePhase.Grid, grid.Phase);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(6), grid.DeadlineUtc);
        Assert.Null(grid.Question);
        Assert.Equal(Enumerable.Range(1, 22), grid.Drivers.Select(d => d.Position));
        Assert.All(grid.Drivers, d => Assert.Equal(DriverStatus.Ready, d.Status));
        Assert.NotEqual(select.Drivers.Select(d => d.Code), grid.Drivers.Select(d => d.Code));   // shuffled start order
    }
}
