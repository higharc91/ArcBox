using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core;

/// <summary>Something the server (or the connection) told the host. Raised on a pool thread.</summary>
public abstract record HostEvent;

/// <summary><c>LobbyChanged(lobby)</c>: the full lobby, sent on every change and after <c>ResumeHost</c>.</summary>
public sealed record LobbyEvent(LobbyState Lobby) : HostEvent;

/// <summary><c>HostView(gameId, view)</c>: the game's latest TV view, kept as JSON so the shell stays game-agnostic.</summary>
public sealed record HostViewEvent(string GameId, JsonElement View) : HostEvent;

/// <summary><c>GameEnded(results)</c>.</summary>
public sealed record GameEndedEvent(GameResults Results) : HostEvent;

/// <summary><c>RoomClosed(reason)</c>, or the room vanished while the host was away (resume failed).</summary>
public sealed record RoomClosedEvent(string Reason) : HostEvent;

/// <summary>
/// <c>ContentRequired(gameId, packs)</c>: the content packs the selected game needs cached before it can be shown (empty for a
/// game without any). Sent on select, on start and after <c>ResumeHost</c>. The shell passes each pack to
/// <see cref="ContentCache.EnsureAsync"/>.
/// </summary>
public sealed record ContentRequiredEvent(string GameId, IReadOnlyList<PackRef> Packs) : HostEvent;

/// <summary>The connection changed state; <see cref="Error"/> is the last failure, if any.</summary>
public sealed record ConnectionEvent(HostConnectionState State, string? Error) : HostEvent;

public enum HostConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}
