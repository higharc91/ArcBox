namespace Meridian.Logic;

/// <summary>
/// Plays a seat: disconnected players (spec §3.3), the simulator, and scenarios' roles. It is handed
/// <see cref="Visibility.For"/> of its seat, never the raw state, so it can't act on what its seat can't see.
/// </summary>
public interface IBotPolicy
{
    /// <summary>
    /// Picks one of <paramref name="legal"/>. <paramref name="rng"/> is the bot's own generator, never the state's, so a
    /// bot's decisions never change what the game deals.
    /// </summary>
    Command Choose(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng);
}
