using ArcBox.Sdk.Content;
using ArcBox.Server.Content;
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
    public static IServiceCollection AddMeridian(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Lazy<MeridianPack>(() =>
            global::Meridian.Logic.PackLoader.Load(PhysicalFileSource.Instance, BasePack(sp))));
        services.AddHostedService<MeridianWarmup>();
        services.AddArcGame(sp => new MeridianGame(sp.GetRequiredService<Lazy<MeridianPack>>()));
        return services;
    }

    /// <summary>Where the base pack is: <c>&lt;Content:Root&gt;/meridian/base</c>, from the content root.</summary>
    private static string BasePack(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<ContentOptions>>().Value;
        var root = Path.GetFullPath(options.Root, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
        return Path.Combine(root, MeridianGame.Id, "base");
    }
}
