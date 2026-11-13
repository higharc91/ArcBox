namespace Meridian.Logic;

/// <summary>
/// Plays a seat: disconnected players (spec §3.3), the simulator, and later the scripted ship. A bot may only look at
/// what its seat can see; in M1 everything is public, and stage M3's audiences will enforce it.
/// </summary>
public interface IBotPolicy
{
    /// <summary>
    /// Picks one of <paramref name="legal"/>. <paramref name="rng"/> is the bot's own generator, never the state's, so a
    /// bot's decisions never change what the game deals.
    /// </summary>
    Command Choose(RulesPack pack, GameState state, SeatId seat, EqArray<Command> legal, ref Rng rng);
}
