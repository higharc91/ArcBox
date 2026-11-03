namespace ArcBox.Sdk.Protocol;

public static class ClientMethods
{
    public const string LobbyChanged = "LobbyChanged";
    public const string HostView = "HostView";
    /// <summary>
    /// Sent to a TV that asked for patches (<c>UseHostPatches</c>): (gameId, patch), the change from the view it has to the game's
    /// latest (<see cref="HostViewPatch"/>). A TV that can't apply one asks for the whole view again (<c>ResendHostView</c>).
    /// </summary>
    public const string HostViewPatch = "HostViewPatch";
    public const string ControllerView = "ControllerView";
    public const string GameEnded = "GameEnded";
    public const string RoomClosed = "RoomClosed";
    public const string Kicked = "Kicked";
    /// <summary>Sent to the TV: (gameId, <see cref="PackRef"/>[]), the content packs it must have cached.</summary>
    public const string ContentRequired = "ContentRequired";
}
