using System.Globalization;
using System.Text;
using Meridian.Logic;

namespace Meridian.Sim;

/// <summary>One bot game played to its outcome with a scenario forced.</summary>
public sealed record EndgameGame(
    string Scenario, int Players, bool Ended, bool CrewWon, int Winners, int FirstWinnerSeat, bool ByLimit,
    int ExploreTurns, int EndgameTurns, int Deaths);

/// <summary>Plays whole games per scenario and reports against spec §7.3 (M3 plan, Task 10).</summary>
public static class EndgameReport
{
    // Seconds a turn takes at the table, for the length estimate: exploring turns are quicker than endgame turns, and the
    // Breach sequence (briefings) is read once. Assumptions, stated in the report.
    public const double ExploreTurnSeconds = 40, EndgameTurnSeconds = 55, BriefingSeconds = 90;

    public static IReadOnlyList<EndgameGame> Run(RulesPack basePack, string scenario, int games, int minPlayers, int maxPlayers, ulong seed)
    {
        var pack = basePack.WithForcedScenario(scenario);
        var results = new List<EndgameGame>();
        for (var players = minPlayers; players <= maxPlayers; players++)
        {
            for (var i = 0; i < games; i++)
            {
                results.Add(Play(pack, scenario, players, seed + (ulong)i));
            }
        }
        return results;
    }

    private static EndgameGame Play(RulesPack pack, string scenario, int players, ulong seed)
    {
        var state = Rules.NewGame(pack, BotRunner.Seats(pack, players), seed);
        int exploreTurns = 1, endgameTurns = 0, deaths = 0;
        var byLimit = false;
        foreach (var step in BotRunner.PlayToEnd(pack, state, seed, 6000))
        {
            var turns = step.Result.Events.Count(e => e is TurnStarted);
            if (state.Phase == Phase.Explore)
            {
                exploreTurns += turns;
            }
            else
            {
                endgameTurns += turns;
            }
            deaths += step.Result.Events.Count(e => e is Died);
            if (step.Result.Events.OfType<GameOver>().Any())
            {
                byLimit = step.Result.Events.OfType<RoundClosed>().Any(r =>
                    r.Round - step.Result.State.Board!.StartRound + 1 >= step.Result.State.Board.RoundLimit)
                    && pack.Scenarios.Get(scenario).Evaluate(pack, step.Result.State with { Phase = Phase.Endgame }) is null;
            }
            state = step.Result.State;
        }
        var outcome = state.Board?.Outcome;
        var crewSide = state.Board?.Roles.Where(r => r.Side == "crew").Select(r => r.Seat).ToHashSet() ?? [];
        var winners = outcome?.Winners ?? [];
        var crewWon = crewSide.Count > 0 ? winners.Any(crewSide.Contains) : winners.Count > 0;
        // Turn-order position from the endgame's first turn (the seat after the Breach's), which is what fairness is about.
        var opener = state.Breach is { } b ? (state.IndexOf(b.Seat) + 1) % players : 0;
        var first = winners.Count > 0 ? (state.IndexOf(winners[0]) - opener + players) % players : -1;
        return new EndgameGame(scenario, players, outcome is not null, crewWon, winners.Count, first, byLimit, exploreTurns, endgameTurns, deaths);
    }

    public static string Format(IReadOnlyList<EndgameGame> games)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("| Scenario | Players | Games | Ended | Win rate | Round limit decided | Breach at (share of turns, median) | Endgame turns (median) | Deaths per game | Est. minutes (median) |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var group in games.GroupBy(g => (g.Scenario, g.Players)).OrderBy(g => g.Key.Scenario).ThenBy(g => g.Key.Players))
        {
            var g = group.ToList();
            string Pct(Func<EndgameGame, bool> f) => (100.0 * g.Count(f) / g.Count).ToString("0", inv) + "%";
            var breachShare = Median(g.Select(x => (double)x.ExploreTurns / Math.Max(1, x.ExploreTurns + x.EndgameTurns)));
            var minutes = Median(g.Select(x => (x.ExploreTurns * ExploreTurnSeconds + x.EndgameTurns * EndgameTurnSeconds + BriefingSeconds) / 60));
            var win = WinRate(group.Key.Scenario, g);
            text.AppendLine(inv,
                $"| {group.Key.Scenario} | {group.Key.Players} | {g.Count} | {Pct(x => x.Ended)} | {win} | {Pct(x => x.ByLimit)} | " +
                $"{(breachShare * 100).ToString("0", inv)}% | {Median(g.Select(x => (double)x.EndgameTurns)).ToString("0", inv)} | " +
                $"{g.Average(x => x.Deaths).ToString("0.0", inv)} | {minutes.ToString("0", inv)} |");
        }
        return text.ToString();
    }

    /// <summary>
    /// What "win rate" means differs by archetype: the crew's side for traitor and co-op scenarios, the share of seats
    /// that survive for the escape, and for the free-for-all the largest seat position's share against a fair share.
    /// </summary>
    private static string WinRate(string scenario, List<EndgameGame> g)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (scenario)
        {
            case DecayingOrbit.Id:
                return "survive " + (100.0 * g.Sum(x => x.Winners) / g.Sum(x => x.Players)).ToString("0", inv) + "%";
            case SalvageRights.Id:
                var won = g.Where(x => x.FirstWinnerSeat >= 0).ToList();
                if (won.Count == 0)
                {
                    return "nobody won";
                }
                var fair = 1.0 / g[0].Players;
                var worst = won.GroupBy(x => x.FirstWinnerSeat).Max(s => (double)s.Count() / won.Count);
                return $"{(100.0 * won.Count / g.Count).ToString("0", inv)}% claimed; top turn position {(worst / fair).ToString("0.00", inv)}x fair";
            default:
                return "crew " + (100.0 * g.Count(x => x.CrewWon) / g.Count).ToString("0", inv) + "%";
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
