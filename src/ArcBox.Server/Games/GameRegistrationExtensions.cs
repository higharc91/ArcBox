using ArcBox.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ArcBox.Server.Games;

public static class GameRegistrationExtensions
{
    public static IServiceCollection AddArcGame<TGame>(this IServiceCollection s) where TGame : IGame, new()
    {
        s.AddSingleton(new GameRegistration(() => new TGame()));
        return s;
    }
}
