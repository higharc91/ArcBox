using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArcBox.Server.Rooms;

public sealed class RoomSweeper(RoomManager manager, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await manager.SweepAsync();
            }
            catch (Exception)
            {
                // Sweeper keeps going
            }
        }
    }
}
