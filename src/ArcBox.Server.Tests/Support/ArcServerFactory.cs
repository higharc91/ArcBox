using ArcBox.Server.Games;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.TestGames;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Support;

/// <summary>The real server in memory. Room time runs on <see cref="Time"/>; the global clock is left alone.</summary>
public sealed class ArcServerFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // AddRedline reads this while Program registers services, so it must be a setting, not a test service.
        builder.UseSetting("Redline:OpenTdb:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAllKeyed<TimeProvider>(RoomManager.ClockKey);
            services.AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, Time);
            services.AddArcGame<ThrowerGame>();
        });
    }
}
