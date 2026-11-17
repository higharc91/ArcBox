using System.Text.Json;

namespace ArcBox.Sdk;

/// <summary>
/// Opt-in for games that resume after a server restart without a campaign (Meridian spec §5.6). The room stores the
/// newest snapshot server-side, never sends it to a client, and hands it back before <see cref="IResumableGame.Resume"/>.
/// The platform never reads inside it: a snapshot is the game's own JSON, at most 256 KB.
/// </summary>
public interface ISnapshotGame : IResumableGame
{
    /// <summary>
    /// Called after each guarded call (an input, a host signal, a connection change, a timer, the start). Return null
    /// when nothing worth saving changed, so a game can save once a turn rather than at every call.
    /// </summary>
    JsonElement? TakeSnapshot();

    /// <summary>
    /// Called before <see cref="IResumableGame.Resume"/> with the last stored snapshot. Throw if it can't be read (an older
    /// build's format, say): the room then goes back to the lobby with "Game interrupted", as if there were none.
    /// </summary>
    void Restore(JsonElement snapshot);
}
