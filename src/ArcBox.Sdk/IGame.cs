namespace ArcBox.Sdk;

public interface IGame
{
    GameManifest Manifest { get; }                      // Id, Name, MinPlayers, MaxPlayers, Description
    void Start(IGameContext ctx, IReadOnlyList<Player> players);
    void OnInput(PlayerId from, PlayerInput input);    // payload already matched to the current ViewId
    void OnTimer(string key);
    void OnPlayerConnectionChanged(PlayerId id, bool connected);
    object HostView();                                  // a game-specific record, e.g. PromptDuelHostView
    ControllerView PlayerView(PlayerId id);
}
