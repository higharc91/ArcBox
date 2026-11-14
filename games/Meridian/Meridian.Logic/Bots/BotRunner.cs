namespace Meridian.Logic;

/// <summary>One step of a bot game: who acted, with what, and what came of it.</summary>
public sealed record BotStep(SeatId Seat, Command Command, StepResult Result);

/// <summary>Plays a game with bots in every seat, one command at a time. Used by tests and the simulator.</summary>
public static class BotRunner
{
    /// <summary>The seat the game is waiting on: whoever owes an answer, else the active seat.</summary>
    public static SeatId Actor(GameState state) => state.Waiting?.From ?? state.ActiveSeat;

    /// <summary>
    /// Steps until the Breach fires or <paramref name="maxCommands"/> run out. The bots share one generator seeded from
    /// <paramref name="botSeed"/>, separate from the game's.
    /// </summary>
    public static IEnumerable<BotStep> Play(RulesPack pack, GameState state, IBotPolicy bot, ulong botSeed, int maxCommands)
    {
        var rng = Rng.Seeded(botSeed, stream: 1013);
        for (var i = 0; i < maxCommands && state.Phase == Phase.Explore; i++)
        {
            var seat = Actor(state);
            // A bot sees only what its seat sees (spec §6): never the raw state.
            var command = bot.Choose(pack, Visibility.For(state, seat), seat, Rules.Legal(pack, state, seat), ref rng);
            var result = Rules.Step(pack, state, command);
            state = result.State;
            yield return new BotStep(seat, command, result);
        }
    }

    /// <summary>
    /// Plays a whole game with bots: the Explore bot until the Breach, then <see cref="Begin"/>, then each seat's role bot
    /// from the scenario, until a side wins or <paramref name="maxCommands"/> run out.
    /// </summary>
    public static IEnumerable<BotStep> PlayToEnd(RulesPack pack, GameState state, ulong botSeed, int maxCommands)
    {
        var rng = Rng.Seeded(botSeed, stream: 1013);
        for (var i = 0; i < maxCommands && state.Phase != Phase.Over; i++)
        {
            Command command;
            SeatId seat;
            if (state.Phase == Phase.Breach)
            {
                if (state.Board is null)
                {
                    yield break;   // no scenario registered: the game ends at the Breach, as in stage M2
                }
                seat = state.ActiveSeat;
                command = new Begin(seat);
            }
            else
            {
                seat = Actor(state);
                IBotPolicy bot = state.Phase == Phase.Endgame && state.RoleOf(seat) is { } role
                    ? pack.Scenarios.Get(state.Board!.Scenario).Bot(role.Role)
                    : ExploreBot.Instance;
                command = bot.Choose(pack, Visibility.For(state, seat), seat, Rules.Legal(pack, state, seat), ref rng);
            }
            var result = Rules.Step(pack, state, command);
            state = result.State;
            yield return new BotStep(seat, command, result);
        }
    }

    /// <summary>The first <paramref name="players"/> crew in pack order, in seats s1, s2, ….</summary>
    public static IReadOnlyList<(SeatId Seat, CrewId Crew)> Seats(RulesPack pack, int players)
        => pack.Crew.Take(players).Select((c, i) => (new SeatId($"s{i + 1}"), c.Id)).ToList();
}
