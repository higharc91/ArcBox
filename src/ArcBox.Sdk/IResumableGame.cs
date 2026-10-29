using ArcBox.Sdk.Campaigns;

namespace ArcBox.Sdk;

/// <summary>Implemented by games whose manifest sets ResumesAfterRestart. Called instead of Start.</summary>
public interface IResumableGame
{
    /// <summary>
    /// Rebuild from the game's own durable state. campaign is set for campaign games (re-checked-out by the room).
    /// Every player starts disconnected and reconnects through OnPlayerConnectionChanged. Wait for them (e.g. a
    /// "Resuming…" phase) rather than handing every seat to an AI.
    /// </summary>
    void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign);
}
