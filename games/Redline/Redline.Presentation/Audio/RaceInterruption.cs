using Redline.Logic;

namespace Redline.Presentation.Audio;

/// <summary>
/// Issue #24: whether the race the TV is taking off the air was cut off (a server restart, the host's Esc, the room
/// closing) rather than run to its end. Only a cut-off race has its sound stopped when the scene parks; a finished one
/// keeps today's behaviour, its last line or cork pop ringing out over the results. Kept out of Godot so it can be tested.
/// </summary>
public static class RaceInterruption
{
    /// <param name="lastPhase">The phase of the last view the TV showed, or null when it showed none.</param>
    /// <returns>
    /// False after the Champagne (the podium's last phase) or the Chequered flag (the last phase when nobody takes the
    /// podium; a race cut off during it can't be told apart from one that ended there), and with nothing on air; true
    /// after any other phase.
    /// </returns>
    public static bool WasInterrupted(RacePhase? lastPhase) =>
        lastPhase is { } phase && phase is not (RacePhase.Champagne or RacePhase.Chequered);
}
