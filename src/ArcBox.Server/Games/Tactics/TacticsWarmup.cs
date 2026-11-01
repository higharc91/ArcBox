using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Games.Tactics;

/// <summary>
/// Loads the Tactics rules pack and lore in the background as the server starts, so the first Tactics room doesn't. Loading the pack takes
/// about 1.4 s on an idle 4-core machine, and a room's game call that runs longer than <see cref="RoomOptions.GameCallTimeout"/> (2 s)
/// has the room closed as hung: on a loaded machine (a TV rendering in software on the same box, in the end-to-end runs) the first
/// campaign or muster did exactly that. The server still starts without waiting, and a pack that fails to load fails the first Tactics
/// room with its errors, as before: <see cref="Lazy{T}"/> keeps the exception for it, and here it's only logged.
/// </summary>
internal sealed class TacticsWarmup(Lazy<RulesPack> pack, Lazy<LoreBook> lore, ILogger<TacticsWarmup> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        try
        {
            _ = pack.Value;
            _ = lore.Value;
        }
        catch (Exception e)
        {
            log.LogWarning("The Tactics content didn't load ahead of the first room: {Error}", e.Message);
        }
    }, stoppingToken);
}
