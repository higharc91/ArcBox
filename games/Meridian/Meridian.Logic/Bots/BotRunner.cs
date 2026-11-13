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
            var command = bot.Choose(pack, state, seat, Rules.Legal(pack, state, seat), ref rng);
            var result = Rules.Step(pack, state, command);
            state = result.State;
            yield return new BotStep(seat, command, result);
        }
    }

    /// <summary>The first <paramref name="players"/> crew in pack order, in seats s1, s2, ….</summary>
    public static IReadOnlyList<(SeatId Seat, CrewId Crew)> Seats(RulesPack pack, int players)
        => pack.Crew.Take(players).Select((c, i) => (new SeatId($"s{i + 1}"), c.Id)).ToList();
}
