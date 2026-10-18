namespace ArcBox.Sdk.Protocol;

public sealed record CreateRoomResult(string Code, string HostToken);
public sealed record JoinResult(string Code, PlayerId PlayerId, string Secret);
public sealed record LobbyPlayer(PlayerId Id, string Name, bool IsVip, bool Connected);
public sealed record LobbyState(
    string Code,
    IReadOnlyList<LobbyPlayer> Players,
    string? SelectedGameId,
    IReadOnlyList<GameManifest> Games,
    bool InGame,
    bool CanStart,
    GameResults? LastResults);
