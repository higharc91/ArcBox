using ArcBox.Sdk;

namespace Redline.Logic;

/// <summary>Race classification (§2.3), the Chequered-flag awards (§2.4) and the humans-only results.</summary>
public static class Ranking
{
    public const string BoldestDriver = "Boldest driver";
    public const string SharpestMind = "Sharpest mind";
    public const string BiggestCrash = "Biggest crash";

    /// <summary>
    /// Sorts by Distance desc, then Correct desc. The sort is stable, so full ties keep their input order.
    /// Equal (Distance, Correct) share a position and the next position skips (1, 2, 2, 4).
    /// </summary>
    public static IReadOnlyList<(Driver Driver, int Position)> Rank(IEnumerable<Driver> field)
    {
        var sorted = field.OrderByDescending(d => d.Distance).ThenByDescending(d => d.Correct).ToList();
        var ranked = new List<(Driver, int)>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var tied = i > 0 && sorted[i].Distance == sorted[i - 1].Distance && sorted[i].Correct == sorted[i - 1].Correct;
            ranked.Add((sorted[i], tied ? ranked[i - 1].Item2 : i + 1));
        }
        return ranked;
    }

    /// <summary>
    /// §2.4, humans only: the most Redline picks, correct answers and spin-outs, each needing at least 1.
    /// Ties go to the better-ranked driver. Unearned awards are omitted.
    /// </summary>
    /// <param name="ranked">The final classification from <see cref="Rank"/>.</param>
    /// <param name="rounds">Rounds actually played, for the "{n} of {rounds} right" detail.</param>
    public static IReadOnlyList<Award> Awards(IReadOnlyList<(Driver Driver, int Position)> ranked, int rounds)
    {
        var humans = ranked.Select(r => r.Driver).Where(d => d.IsHuman).ToList();
        var awards = new List<Award>(3);
        Add(BoldestDriver, d => d.Redlines, n => n == 1 ? "1 Redline" : $"{n} Redlines");
        Add(SharpestMind, d => d.Correct, n => $"{n} of {rounds} right");
        Add(BiggestCrash, d => d.SpinOuts, n => n == 1 ? "1 spin-out" : $"{n} spin-outs");
        return awards;

        void Add(string title, Func<Driver, int> count, Func<int, string> detail)
        {
            Driver? best = null;
            foreach (var d in humans)                                  // ranked order: strict > keeps the better-ranked on a tie
                if (count(d) >= 1 && (best is null || count(d) > count(best))) best = d;
            if (best is not null) awards.Add(new Award(title, best.PlayerId!.Value, best.PlayerName ?? "", detail(count(best))));
        }
    }

    /// <summary>
    /// §2.3: the human players in race order, as "{Player} ({CODE})" with their distance, and the winner message.
    /// </summary>
    public static GameResults Results(IReadOnlyList<(Driver Driver, int Position)> ranked)
    {
        var humans = ranked.Where(r => r.Driver.IsHuman).ToList();
        var scores = humans.Select(r => new PlayerScore(r.Driver.PlayerId!.Value, Label(r.Driver), r.Driver.Distance)).ToList();
        return new GameResults(scores, Message(ranked, humans));
    }

    private static string? Message(IReadOnlyList<(Driver Driver, int Position)> ranked, IReadOnlyList<(Driver Driver, int Position)> humans)
    {
        if (ranked.Count == 0) return null;

        var humanWinners = humans.Where(r => r.Position == 1).Select(r => Label(r.Driver)).ToList();
        switch (humanWinners.Count)
        {
            case 1: return $"{humanWinners[0]} wins the race!";
            case > 1: return $"{string.Join(", ", humanWinners.Take(humanWinners.Count - 1))} and {humanWinners[^1]} share the win!";
        }

        var winner = $"{Surname(ranked[0].Driver)} (AI) wins the race";
        if (humans.Count == 0) return $"{winner}!";
        var best = humans[0];
        return $"{winner} · best player: {Label(best.Driver)}, P{best.Position}";
    }

    /// <summary>"{Player} ({CODE})", e.g. "JO (NOR)".</summary>
    private static string Label(Driver d) => $"{d.PlayerName} ({d.Code})";

    /// <summary>The last whitespace-separated token of the driver's full name.</summary>
    private static string Surname(Driver d) =>
        d.Seat.Driver.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is [.., var last] ? last : d.Seat.Driver.Name;
}
