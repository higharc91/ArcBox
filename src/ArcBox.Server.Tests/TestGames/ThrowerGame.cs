using ArcBox.Sdk;

namespace ArcBox.Server.Tests.TestGames;

public sealed class ThrowerGame : IGame
{
    public static bool ThrowOnStart { get; set; }

    private IGameContext? _ctx;

    public GameManifest Manifest { get; } = new("thrower", "Thrower", "", 1, 8);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        if (ThrowOnStart)
        {
            throw new InvalidOperationException("ThrowOnStart");
        }
        _ctx = ctx;
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        var buttons = input.Read<ButtonsInput>();
        if (buttons.Pressed == "boom")
        {
            throw new InvalidOperationException("boom");
        }
        if (buttons.Pressed == "end")
        {
            _ctx?.EndGame(new GameResults([]));
        }
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public object HostView() => new { };

    public ControllerView PlayerView(PlayerId id) =>
        new ButtonsView(null, [new Option("boom", "Boom"), new Option("end", "End")]);
}
