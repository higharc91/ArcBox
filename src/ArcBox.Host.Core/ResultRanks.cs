using ArcBox.Sdk;

namespace ArcBox.Host.Core;

/// <summary>A row of the TV's Results screen: the place shown and whether it gets the WINNER badge.</summary>
public readonly record struct ResultRank(int Place, bool Winner);

/// <summary>
/// Places for the Results screen. <see cref="GameResults.Scores"/> is already in the game's final order.
/// <list type="bullet">
/// <item>With no <see cref="GameResults.Message"/>, the scores are the whole story: equal scores share a place
/// (1, 1, 3) and every first place is a winner.</item>
/// <item>With a message, the game has its own tie-breaks and says who won (REDLINE: "Antonelli (AI) wins the race ·
/// best player: …", with players on equal distance split by correct answers), so the places follow the list, as on
/// the phones, and the header carries the outcome instead of a badge.</item>
/// </list>
/// </summary>
public static class ResultRanks
{
    public static IReadOnlyList<ResultRank> Compute(GameResults results)
    {
        var scores = results.Scores;
        var ordered = !string.IsNullOrWhiteSpace(results.Message);
        var ranks = new List<ResultRank>(scores.Count);
        for (var i = 0; i < scores.Count; i++)
        {
            var place = !ordered && i > 0 && scores[i].Score == scores[i - 1].Score ? ranks[i - 1].Place : i + 1;
            ranks.Add(new ResultRank(place, !ordered && place == 1));
        }
        return ranks;
    }
}
