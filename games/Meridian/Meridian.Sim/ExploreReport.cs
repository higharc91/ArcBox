using System.Globalization;
using System.Text;
using Meridian.Logic;

namespace Meridian.Sim;

public sealed record ExploreOptions(int Games, int MinPlayers, int MaxPlayers, ulong Seed, int MaxCommands = 400);

/// <summary>One bot game, played to the Breach.</summary>
public sealed record ExploreGame(
    int Players, bool Reached, int AnomalyAtBreach, bool ForcedByAnomaly, bool ForcedByShip, int Rounds, int Turns,
    int TilesPlaced, bool DeckRanOut, double CardsPerPlayer, int LowestStep);

/// <summary>Plays bot games to the Breach and summarises them per player count (plan Task 11).</summary>
public static class ExploreReport
{
    public static IReadOnlyList<ExploreGame> Run(RulesPack pack, ExploreOptions options)
    {
        var games = new List<ExploreGame>();
        for (var players = options.MinPlayers; players <= options.MaxPlayers; players++)
        {
            for (var i = 0; i < options.Games; i++)
            {
                var seed = options.Seed + (ulong)i;
                games.Add(Play(pack, players, seed, options.MaxCommands));
            }
        }
        return games;
    }

    private static ExploreGame Play(RulesPack pack, int players, ulong seed, int maxCommands)
    {
        var state = Rules.NewGame(pack, BotRunner.Seats(pack, players), seed);
        var turns = 1;
        var cards = 0;
        var lowest = state.Crew.Min(c => Traits.All.Min(t => c.Steps[t]));
        foreach (var step in BotRunner.Play(pack, state, ExploreBot.Instance, seed, maxCommands))
        {
            state = step.Result.State;
            turns += step.Result.Events.Count(e => e is TurnStarted);
            cards += step.Result.Events.Count(e => e is CardDrawn);
            lowest = Math.Min(lowest, state.Crew.Min(c => Traits.All.Min(t => c.Steps[t])));
        }

        var breach = state.Breach;
        var deckRanOut = Enum.GetValues<DeckId>().Any(d => !state.TilePile.Any(t => pack.Tile(t).Decks.Contains(d)));
        return new ExploreGame(
            players,
            Reached: breach is not null,
            AnomalyAtBreach: state.AnomaliesDrawn,
            ForcedByAnomaly: breach is { Forced: true, Anomaly: not null },
            ForcedByShip: breach is { Anomaly: null },
            state.Round,
            turns,
            TilesPlaced: state.Ship.Compartments.Count - pack.Start.Place.Count,
            deckRanOut,
            CardsPerPlayer: (double)cards / players,
            lowest);
    }

    /// <summary>A Markdown table per player count.</summary>
    public static string Format(RulesPack pack, IReadOnlyList<ExploreGame> games)
    {
        var placeable = pack.Tiles.Sum(t => t.Copies);
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        text.AppendLine(inv, $"Breach threshold {pack.Rules.BreachThreshold}; die faces {pack.Rules.DieFaces}; forced on Anomaly {pack.Rules.ForcedBreachAnomaly}; {placeable} placeable tiles.");
        text.AppendLine();
        text.AppendLine("| Players | Games | Reached | Anomaly at Breach (p10 / median / p90) | Forced: 9th Anomaly | Forced: ship full | Rounds (median) | Turns (median) | Tiles placed (median) | Breach at 55–75% of tiles | A deck ran out | Cards per player | Lowest step |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var group in games.GroupBy(g => g.Players).OrderBy(g => g.Key))
        {
            var g = group.ToList();
            var reached = g.Where(x => x.Reached).ToList();
            string Pct(Func<ExploreGame, bool> f) => (100.0 * g.Count(f) / g.Count).ToString("0.0", inv) + "%";
            var anomalies = reached.Select(x => x.AnomalyAtBreach).Order().ToList();
            var inBand = reached.Count(x => x.TilesPlaced >= 0.55 * placeable && x.TilesPlaced <= 0.75 * placeable);
            text.AppendLine(inv,
                $"| {group.Key} | {g.Count} | {Pct(x => x.Reached)} | {Q(anomalies, 0.1)} / {Q(anomalies, 0.5)} / {Q(anomalies, 0.9)} | " +
                $"{Pct(x => x.ForcedByAnomaly)} | {Pct(x => x.ForcedByShip)} | {Q(reached.Select(x => x.Rounds).Order().ToList(), 0.5)} | " +
                $"{Q(reached.Select(x => x.Turns).Order().ToList(), 0.5)} | {Q(reached.Select(x => x.TilesPlaced).Order().ToList(), 0.5)} | " +
                $"{(100.0 * inBand / Math.Max(1, reached.Count)).ToString("0.0", inv)}% | {Pct(x => x.DeckRanOut)} | " +
                $"{g.Average(x => x.CardsPerPlayer).ToString("0.0", inv)} | {g.Min(x => x.LowestStep)} |");
        }
        return text.ToString();
    }

    private static string Q(IReadOnlyList<int> sorted, double q)
        => sorted.Count == 0 ? "–" : sorted[Math.Clamp((int)Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1)].ToString(CultureInfo.InvariantCulture);
}
