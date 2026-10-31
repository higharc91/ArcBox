using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Speech;
using Microsoft.Extensions.Options;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Games.Tactics;

public static class TacticsServiceCollectionExtensions
{
    /// <summary>
    /// Wires up ArcBox Tactics: its rules pack, loaded once from <c>&lt;Content:Root&gt;/tactics/base</c> (the rules and the
    /// art manifests they're checked against), the game, its campaign rules and its voice themes (one per speaker, for live speech). The pack loads on first use, so the server
    /// starts without it (the registry only reads the manifest); a missing or broken pack fails the first tactics room,
    /// with every error listed.
    /// </summary>
    public static IServiceCollection AddTactics(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Lazy<RulesPack>(() =>
        {
            var options = sp.GetRequiredService<IOptions<ContentOptions>>().Value;
            var root = Path.GetFullPath(options.Root, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
            var basePack = Path.Combine(root, TacticsGame.Id, "base");
            return RulesPack.Load([Path.Combine(basePack, "rules")], ArtManifests.Load(basePack));
        }));
        services.AddVoiceThemes(TacticsVoices.All);
        services.AddArcGame(sp => new TacticsGame(sp.GetRequiredService<Lazy<RulesPack>>()));
        services.AddSingleton<ICampaignRules>(sp => new TacticsCampaignRules(sp.GetRequiredService<Lazy<RulesPack>>()));
        services.AddVoiceThemes(TacticsVoices.All);
        return services;
    }
}
