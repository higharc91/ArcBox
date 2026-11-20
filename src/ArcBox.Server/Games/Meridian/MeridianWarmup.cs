using System.Diagnostics;
using MeridianPack = Meridian.Logic.RulesPack;

namespace ArcBox.Server.Games.Meridian;

/// <summary>
/// Loads the Meridian pack as the server starts, so no room's first game call waits on it. A pack that fails is logged
/// here and still fails the first Meridian room, because <see cref="Lazy{T}"/> keeps the exception (as for Tactics).
/// </summary>
internal sealed class MeridianWarmup(Lazy<MeridianPack> pack, ILogger<MeridianWarmup> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var clock = Stopwatch.StartNew();
            _ = pack.Value;
            log.LogInformation("Meridian content loaded in {Ms} ms", clock.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "The Meridian content didn't load ahead of the first room");
        }
    }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
