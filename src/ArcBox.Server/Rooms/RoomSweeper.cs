using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArcBox.Server.Rooms;

/// <summary>
/// Every second on the rooms clock: the watchdog and idle parking (<see cref="RoomManager.SweepAsync"/>). Once a minute
/// it also deletes stored rooms older than <see cref="RoomOptions.RetainFor"/> (see <see cref="RoomManager.DeleteExpiredRowsAsync"/>).
/// </summary>
public sealed class RoomSweeper(RoomManager manager, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time) : BackgroundService
{
    public static readonly TimeSpan DeleteRowsEvery = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        DateTimeOffset? rowsDeleted = null;
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

            var now = time.GetUtcNow();
            if (rowsDeleted is null || now - rowsDeleted >= DeleteRowsEvery)
            {
                rowsDeleted = now;
                try
                {
                    await manager.DeleteExpiredRowsAsync();
                }
                catch (Exception)
                {
                    // Tried again next minute
                }
            }
        }
    }
}
