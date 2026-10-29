using ArcBox.Sdk.Campaigns;

namespace ArcBox.Server.Campaigns;

/// <summary>The campaign rules of every game that uses campaigns, by game id.</summary>
public sealed class CampaignRulesRegistry(IEnumerable<ICampaignRules> rules)
{
    private readonly Dictionary<string, ICampaignRules> _byGame = rules.ToDictionary(r => r.GameId, StringComparer.Ordinal);

    /// <summary>The rules for <paramref name="gameId"/>, or null when that game doesn't use campaigns.</summary>
    public ICampaignRules? For(string gameId) => _byGame.GetValueOrDefault(gameId);
}
