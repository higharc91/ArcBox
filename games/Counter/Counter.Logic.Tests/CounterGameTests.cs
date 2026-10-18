using ArcBox.Sdk.Testing;
using Counter.Logic;
using Xunit;

namespace Counter.Logic.Tests;

public class CounterGameTests
{
    [Fact]
    public void Start_sets_end_timer()
    {
        var h = new GameHarness<CounterGame>(new());
        h.Start();
        Assert.Contains("end", h.ActiveTimers);
        Assert.Equal(h.Now + CounterGame.Duration, h.HostView<CounterHostView>().EndsAt);
    }

    [Fact]
    public void Press_increments_only_that_player()
    {
        var h = new GameHarness<CounterGame>(new());
        h.Start();
        h.Input(0, new { pressed = "press" });
        h.Input(0, new { pressed = "press" });
        h.Input(2, new { pressed = "press" });
        Assert.Equal([2, 0, 1, 0], h.HostView<CounterHostView>().Rows.Select(r => r.Count));
    }

    [Fact]
    public void Unknown_button_and_malformed_payload_are_ignored()
    {
        var h = new GameHarness<CounterGame>(new());
        h.Start();
        h.Input(0, new { pressed = "nope" });
        h.Input(0, new { wrong = 5 });
        Assert.All(h.HostView<CounterHostView>().Rows, r => Assert.Equal(0, r.Count));
    }

    [Fact]
    public void Player_view_is_stable_between_presses()
    {
        var h = new GameHarness<CounterGame>(new());
        h.Start();
        var before = h.PlayerView(0);
        h.Input(0, new { pressed = "press" });
        Assert.Equal(before, h.PlayerView(0));
    }

    [Fact]
    public void Timer_ends_game_with_sorted_scores()
    {
        var h = new GameHarness<CounterGame>(new(), playerCount: 3);
        h.Start();
        h.Input(1, new { pressed = "press" });
        h.Advance(CounterGame.Duration);
        Assert.True(h.IsOver);
        Assert.Equal(["Player2", "Player1", "Player3"], h.Results!.Scores.Select(s => s.Name));
        h.Input(0, new { pressed = "press" });                       // no-op after end
        Assert.Equal(0, h.Results.Scores.Single(s => s.Name == "Player1").Score);
    }

    [Fact]
    public void Disconnected_player_keeps_count()
    {
        var h = new GameHarness<CounterGame>(new());
        h.Start();
        h.Input(0, new { pressed = "press" });
        h.SetConnected(0, false);
        Assert.Equal(1, h.HostView<CounterHostView>().Rows[0].Count);
    }
}
