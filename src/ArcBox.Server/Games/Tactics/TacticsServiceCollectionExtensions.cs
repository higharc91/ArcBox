using System.Collections.Immutable;
using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Speech;
using Microsoft.Extensions.Options;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Games.Tactics;

public static class TacticsServiceCollectionExtensions
{
    /// <summary>The configuration section of a Development seed (<see cref="TacticsSeed"/>).</summary>
    public const string SeedSection = "Tactics:Seed";

    /// <summary>
    /// The Development switch that offers the VIP the battle list from the world (<c>"Tactics": { "BattleList": true }</c>). Off unless
    /// it's set, and always off outside Development: in play, fights happen where the party walks into them (the user's decision).
    /// </summary>
    public const string BattleListSetting = "Tactics:BattleList";

    /// <summary>
    /// Wires up ArcBox Tactics: its rules pack, loaded once from <c>&lt;Content:Root&gt;/tactics/base</c> (the rules and the
    /// art manifests they're checked against), its lore from the same folder, the game, its campaign rules and its voice themes (one per
    /// speaker, for live speech). The pack and the lore load as the server starts, before it takes connections (<see cref="TacticsWarmup"/>),
    /// so no room's game call waits on them; a missing or broken pack doesn't stop the server, and fails the first tactics room, with every
    /// error listed. In Development only, a
    /// <c>Tactics:Seed</c> section starts new campaigns part way through their story (<see cref="SeedFrom"/>), and
    /// <c>Tactics:BattleList</c> offers the battle list from the world (<see cref="BattleListSetting"/>).
    /// </summary>
    public static IServiceCollection AddTactics(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Lazy<RulesPack>(() =>
        {
            var basePack = BasePack(sp);
            return RulesPack.Load([Path.Combine(basePack, "rules")], ArtManifests.Load(basePack));
        }));
        services.AddSingleton(sp => new Lazy<LoreBook>(() => LoreBook.Load(BasePack(sp))));
        services.AddHostedService<TacticsWarmup>();
        services.AddArcGame(sp =>
        {
            var log = sp.GetRequiredService<ILogger<TacticsGame>>();
            // The seed and the battle list are read in Development only.
            var devSettings = sp.GetRequiredService<IHostEnvironment>().IsDevelopment() ? sp.GetService<IConfiguration>() : null;
            var seed = devSettings is null ? null : SeedFrom(devSettings.GetSection(SeedSection));
            var battleList = devSettings?.GetValue<bool>(BattleListSetting) ?? false;
            return new TacticsGame(sp.GetRequiredService<Lazy<RulesPack>>(), lore: sp.GetRequiredService<Lazy<LoreBook>>(), seed: seed, battleList: battleList)
            {
                BattleDifficulty = devSettings?.GetValue<Difficulty?>("Tactics:Difficulty") ?? Difficulty.Normal,
                BattleSeed = devSettings?.GetValue<ulong?>("Tactics:BattleSeed"),
                FaultLogged = fault => LogFault(log, fault),
                StoryLogged = problem => log.LogWarning("Tactics story: {Problem}", problem),
            };
        });
        services.AddSingleton<ICampaignRules>(sp => new TacticsCampaignRules(sp.GetRequiredService<Lazy<RulesPack>>()));
        services.AddVoiceThemes(TacticsVoices.All);
        return services;
    }

    /// <summary>
    /// The seed a <c>Tactics:Seed</c> section describes, or null if there's no such section: <c>Flags</c> and <c>Beats</c> are lists of
    /// ids, <c>Region</c> a region id and <c>At</c> a tile as <c>[x, y]</c>, each optional. For example, in
    /// <c>appsettings.Development.json</c>, <c>"Tactics": { "Seed": { "Flags": ["ch1_skarrow_slain"], "Beats": ["ch1_mayors_request",
    /// "ch1_the_ford_choice"] } }</c> starts a new campaign with the assault on the ford on offer. Throws
    /// <see cref="InvalidOperationException"/> for an <c>At</c> that isn't a tile.
    /// </summary>
    public static TacticsSeed? SeedFrom(IConfigurationSection section)
    {
        if (!section.Exists())
            return null;
        var at = section.GetSection("At").Get<int[]>();
        if (at is not (null or { Length: 0 } or { Length: 2 }))
            throw new InvalidOperationException($"{SeedSection}:At must be a tile, [x, y].");
        return new TacticsSeed(Ids(section, "Flags"), Ids(section, "Beats"), section["Region"], at is { Length: 2 } ? new Tile(at[0], at[1]) : null);
    }

    private static ImmutableArray<string> Ids(IConfigurationSection section, string name) => [.. section.GetSection(name).Get<string[]>() ?? []];

    /// <summary>Where the base pack is: <c>&lt;Content:Root&gt;/tactics/base</c>, from the content root.</summary>
    private static string BasePack(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<ContentOptions>>().Value;
        var root = Path.GetFullPath(options.Root, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
        return Path.Combine(root, TacticsGame.Id, "base");
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
