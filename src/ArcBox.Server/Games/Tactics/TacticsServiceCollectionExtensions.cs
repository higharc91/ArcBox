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
        services.AddArcGame(sp =>
        {
            var log = sp.GetRequiredService<ILogger<TacticsGame>>();
            return new TacticsGame(sp.GetRequiredService<Lazy<RulesPack>>()) { FaultLogged = fault => LogFault(log, fault) };
        });
        services.AddSingleton<ICampaignRules>(sp => new TacticsCampaignRules(sp.GetRequiredService<Lazy<RulesPack>>()));
        services.AddVoiceThemes(TacticsVoices.All);
        return services;
    }

    /// <summary>
    /// An engine failure the game caught and carried on from (rules-engine spec §8), with what reproduces it: the encounter,
    /// seed and beat and the command as an error; the state from before it, which <c>Battle.Apply</c> fails on again, at
    /// Debug, since it runs to tens of KB.
    /// </summary>
    private static void LogFault(ILogger log, EngineFault fault)
    {
        log.LogError(fault.Error,
            "Tactics engine failure ({Where}) in {Encounter}, seed {Seed}, after beat {Beat}, round {Round}, turn {TurnIndex}. Command: {Command}",
            fault.Where, fault.Encounter, fault.Seed, fault.Beat, fault.Round, fault.TurnIndex, fault.Command ?? "none");
        if (fault.State is not null)
            log.LogDebug("Tactics engine failure ({Where}) in {Encounter}, seed {Seed}, after beat {Beat}: state before: {State}",
                fault.Where, fault.Encounter, fault.Seed, fault.Beat, fault.State);
    }
}
