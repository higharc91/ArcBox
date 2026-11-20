using ArcBox.Sdk.Content;
using ArcBox.Server.Content;
using ArcBox.Server.Speech;
using Meridian.Game;
using Microsoft.Extensions.Options;
using MeridianPack = Meridian.Logic.RulesPack;

namespace ArcBox.Server.Games.Meridian;

public static class MeridianServiceCollectionExtensions
{
    /// <summary>
    /// Wires up Derelict: ISV Meridian: its rules pack, loaded once from <c>&lt;Content:Root&gt;/meridian/base</c>, a warmup
    /// that loads it before the server takes connections, and the game. A pack that won't load doesn't stop the server; it
    /// fails the first Meridian room with every content error listed (M2 plan, Task 8).
    /// </summary>
    /// <summary>
    /// The Development setting that makes every Breach pick one scenario (<c>"Meridian": { "Scenario": "the_passenger" }</c>),
    /// for playtests. Ignored outside Development.
    /// </summary>
    public const string ScenarioSetting = "Meridian:Scenario";

    public static IServiceCollection AddMeridian(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Lazy<MeridianPack>(() =>
        {
            var pack = global::Meridian.Logic.PackLoader.Load(PhysicalFileSource.Instance, BasePack(sp));
            var forced = sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? sp.GetService<IConfiguration>()?[ScenarioSetting]
                : null;
            return string.IsNullOrWhiteSpace(forced) ? pack : pack.WithForcedScenario(forced);
        }));
        services.AddHostedService<MeridianWarmup>();
        services.AddVoiceThemes(global::Meridian.Logic.MeridianVoices.All);
        services.AddArcGame(sp => new MeridianGame(sp.GetRequiredService<Lazy<MeridianPack>>()) { CardArt = LogoPath(BasePack(sp)) });
        return services;
    }

    /// <summary>
    /// The lobby card's art (#405): the UI kit's logo, as a path in the base pack (<c>ui/&lt;file&gt;</c>), read from
    /// <c>ui/manifest.json</c>. Null if the kit or its logo isn't there, or the manifest won't read: the text card then.
    /// </summary>
    public static string? LogoPath(string basePack)
    {
        try
        {
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(basePack, "ui", "manifest.json")));
            return manifest.RootElement.TryGetProperty("ui", out var ui) && ui.TryGetProperty("logo", out var logo)
                   && logo.TryGetProperty("file", out var file) && file.GetString() is { Length: > 0 } name
                ? $"ui/{name}"
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Where the base pack is: <c>&lt;Content:Root&gt;/meridian/base</c>, from the content root.</summary>
    private static string BasePack(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<ContentOptions>>().Value;
        var root = Path.GetFullPath(options.Root, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
        return Path.Combine(root, MeridianGame.Id, "base");
    }
}
