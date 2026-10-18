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

public sealed class ArcServerFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Only the rooms' clock is faked. The unkeyed TimeProvider drives SignalR's
            // own keep-alives and timeouts, which must keep running on real time.
            services.RemoveAllKeyed<TimeProvider>(RoomManager.ClockKey);
            services.AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, Time);
            services.AddArcGame<ThrowerGame>();
        });
    }
}
