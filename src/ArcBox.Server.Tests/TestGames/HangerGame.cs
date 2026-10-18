using ArcBox.Sdk;

namespace ArcBox.Server.Tests.TestGames;

public sealed class HangerGame : IGame
{
    public static ManualResetEventSlim Entered { get; private set; } = new(false);
    public static ManualResetEventSlim Gate { get; private set; } = new(false);

    public static void Reset()
    {
        Entered = new ManualResetEventSlim(false);
        Gate = new ManualResetEventSlim(false);
    }

    public GameManifest Manifest { get; } = new("hanger", "Hanger", "", 1, 8);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players) { }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        Entered.Set();
        Gate.Wait();
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public object HostView() => new { };

    public ControllerView PlayerView(PlayerId id) => new WaitView("");
}
