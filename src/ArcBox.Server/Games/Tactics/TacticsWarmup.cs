using System.Diagnostics;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Games.Tactics;

/// <summary>
/// Loads the Tactics rules pack and lore as the server starts, before it takes connections, so that no room's game call has to. Loading
/// the pack takes about 1.4 s on an idle 4-core machine, and a room's game call that runs longer than
/// <see cref="RoomOptions.GameCallTimeout"/> (2 s) has the room closed as hung. On a loaded machine (a TV rendering in software on the same
/// box, in the end-to-end runs) that happened to the first campaign, to Muster, and to a restarted server's room as every phone rejoined at
/// once. A pack that fails to load doesn't stop the server: it's logged here, and it fails the first Tactics room with its errors, as
/// before, since <see cref="Lazy{T}"/> keeps the exception. It logs <c>Tactics content loaded in &lt;n&gt; ms</c> when it's done.
/// </summary>
internal sealed class TacticsWarmup(Lazy<RulesPack> pack, Lazy<LoreBook> lore, ILogger<TacticsWarmup> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var clock = Stopwatch.StartNew();
            _ = pack.Value;
            _ = lore.Value;
            log.LogInformation("Tactics content loaded in {Ms} ms", clock.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "The Tactics content didn't load ahead of the first room");
        }
    }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
