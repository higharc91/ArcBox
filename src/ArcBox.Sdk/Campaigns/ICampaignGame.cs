namespace ArcBox.Sdk.Campaigns;

/// <summary>Implemented by games whose manifest sets UsesCampaigns. The room calls this instead of IGame.Start.</summary>
public interface ICampaignGame
{
    void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign);
}
