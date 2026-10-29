using ArcBox.Sdk.Campaigns;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArcBox.Server.Campaigns;

public static class CampaignServiceCollectionExtensions
{
    /// <summary>The campaign service and the registry of game rules. Needs <c>AddStorage</c> and the rooms' clock.</summary>
    public static IServiceCollection AddCampaigns(this IServiceCollection services)
    {
        services.TryAddSingleton<CampaignRulesRegistry>();
        services.TryAddSingleton<CampaignService>();
        return services;
    }

    /// <summary>Registers a game's campaign rules, the way <c>AddArcGame</c> registers a game.</summary>
    public static IServiceCollection AddCampaignRules<TRules>(this IServiceCollection services) where TRules : class, ICampaignRules
    {
        services.AddSingleton<ICampaignRules, TRules>();
        return services;
    }
}
