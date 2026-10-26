using ArcBox.Sdk;

namespace ArcBox.Host.Core.Tests;

public class ResultRanksTests
{
    private static PlayerScore S(string name, int score) => new(new PlayerId(name), name, score);

    [Fact]
    public void Equal_scores_share_a_place_when_the_scores_are_the_whole_story()
    {
        var ranks = ResultRanks.Compute(new GameResults([S("Ann", 12), S("Bo", 12), S("Cy", 7)]));

        Assert.Equal([new ResultRank(1, true), new ResultRank(1, true), new ResultRank(3, false)], ranks);
    }

    [Fact]
    public void A_game_message_means_the_list_order_is_final_and_names_the_winner()
    {
        // REDLINE: an AI won; Bot2 and Bot3 both covered 3 m but Bot2 answered more correctly, so classified ahead.
        var results = new GameResults(
            [S("Bot2 (HAM)", 3), S("Bot3 (COL)", 3), S("Bot1 (ALB)", 0)],
            "Antonelli (AI) wins the race · best player: Bot2 (HAM), P17");

        var ranks = ResultRanks.Compute(results);

        Assert.Equal([new ResultRank(1, false), new ResultRank(2, false), new ResultRank(3, false)], ranks);
    }

    [Fact]
    public void No_scores_no_rows()
    {
        Assert.Empty(ResultRanks.Compute(new GameResults([], "Nobody finished")));
    }
}
