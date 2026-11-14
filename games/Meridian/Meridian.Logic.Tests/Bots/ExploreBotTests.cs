namespace Meridian.Logic.Tests.Bots;

public sealed class ExploreBotTests
{
    private static RulesPack Pack => BasePack.Pack;

    public static TheoryData<int> PlayerCounts => [3, 4, 5, 6];

    [Theory]
    [MemberData(nameof(PlayerCounts))]
    public void Bots_reach_the_breach_with_no_invariant_broken(int players) => Sweep(players, seeds: 100);

    [Theory]
    [MemberData(nameof(PlayerCounts))]
    [Trait("Category", "Integration")]
    public void A_thousand_bot_games_reach_the_breach_with_no_invariant_broken(int players) => Sweep(players, seeds: 1000);

    private static void Sweep(int players, int seeds)
    {
        var failures = new List<string>();
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            var state = Rules.NewGame(Pack, BotRunner.Seats(Pack, players), seed);
            var steps = 0;
            foreach (var step in BotRunner.Play(Pack, state, ExploreBot.Instance, seed, maxCommands: 400))
            {
                steps++;
                if (step.Result.Events.FirstOrDefault() is Rejected r)
                {
                    failures.Add($"seed {seed} step {steps}: the bot sent an illegal {step.Command}: {r.Reason}");
                    break;
                }
                if (Invariants.Check(Pack, step.Result.State) is { Count: > 0 } broken)
                {
                    failures.Add($"seed {seed} step {steps}: {string.Join("; ", broken)}");
                    break;
                }
                state = step.Result.State;
            }
            if (state.Phase != Phase.Breach && failures.Count == 0)
            {
                failures.Add($"seed {seed}: no Breach within 400 commands");
            }
            if (failures.Count >= 5)
            {
                break;
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void A_bot_game_is_deterministic_for_its_seeds()
    {
        var start = Rules.NewGame(Pack, BotRunner.Seats(Pack, 4), 7);
        GameState End(ulong botSeed) => BotRunner.Play(Pack, start, ExploreBot.Instance, botSeed, 400).Last().Result.State;
        Assert.Equal(End(1), End(1));
        Assert.NotEqual(End(1), End(2));
    }

    [Fact]
    public void The_bot_never_trades()
    {
        var steps = BotRunner.Play(Pack, Rules.NewGame(Pack, BotRunner.Seats(Pack, 6), 3), ExploreBot.Instance, 3, 400);
        Assert.DoesNotContain(steps, s => s.Command is OfferTrade);
    }
}
