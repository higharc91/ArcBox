namespace Meridian.Logic.Tests.Turns;

public sealed class ReplayTests
{
    private static RulesPack Pack => BasePack.Pack;

    [Fact]
    public void A_recorded_game_replays_to_the_same_state()
    {
        var start = Rules.NewGame(Pack, BotRunner.Seats(Pack, 5), 42);
        var played = BotRunner.Play(Pack, start, ExploreBot.Instance, 42, 400).ToList();

        var state = start;
        foreach (var step in played)
        {
            state = Rules.Step(Pack, state, step.Command).State;
        }
        Assert.Equal(played[^1].Result.State, state);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void A_game_saved_and_restored_at_every_step_matches()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var start = Rules.NewGame(Pack, BotRunner.Seats(Pack, 4), seed);
            var played = BotRunner.Play(Pack, start, ExploreBot.Instance, seed, 400).ToList();

            var state = start;
            foreach (var step in played)
            {
                state = Rules.Step(Pack, Json.RoundTrip(state), step.Command).State;
            }
            Assert.Equal(played[^1].Result.State, state);
        }
    }
}
