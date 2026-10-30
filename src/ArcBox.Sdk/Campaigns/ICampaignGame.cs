namespace ArcBox.Sdk.Campaigns;

/// <summary>Implemented by games whose manifest sets UsesCampaigns. The room calls this instead of IGame.Start.</summary>
public interface ICampaignGame
{
    /// <summary>
    /// Starts a session. Every member of the campaign is a player: straight after Start the game hears, through
    /// OnPlayerConnectionChanged, that the ones with no phone yet are disconnected, as it would of a lost phone. The room
    /// holds <paramref name="campaign"/> until the game ends.
    /// </summary>
    void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign);
}
