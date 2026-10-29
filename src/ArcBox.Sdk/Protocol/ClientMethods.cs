namespace ArcBox.Sdk.Protocol;

public static class ClientMethods
{
    public const string LobbyChanged = "LobbyChanged";
    public const string HostView = "HostView";
    public const string ControllerView = "ControllerView";
    public const string GameEnded = "GameEnded";
    public const string RoomClosed = "RoomClosed";
    public const string Kicked = "Kicked";
    /// <summary>Sent to the TV: (gameId, <see cref="PackRef"/>[]), the content packs it must have cached.</summary>
    public const string ContentRequired = "ContentRequired";
}
