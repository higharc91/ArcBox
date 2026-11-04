using ArcBox.Sdk;

namespace ArcBox.Server.Tests.TestGames;

/// <summary>
/// A TV view with a large part that rarely changes (twenty things with ids) and a small part that changes on every press, like
/// the overworld's walk view: so a patch against the TV's view is much smaller than the view (issue #77).
/// </summary>
public sealed class PatchGame : IGame
{
    public const string Id = "patch";

    private int _count;
    private int _moved = -1;

    public GameManifest Manifest { get; } = new(Id, "Patch", "", 1, 8);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players) { }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        var pressed = input.Read<ButtonsInput>().Pressed;
        if (pressed == "press")
            _count++;
        else if (pressed == "move")
            _moved++;
    }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public object HostView() => new
    {
        count = _count,
        note = _count % 3 == 0 ? null : $"pressed {_count}",
        things = Enumerable.Range(0, 20).Select(i => new { id = $"thing_{i:00}", name = $"A rather long name for thing {i}", state = i == _moved ? "open" : "closed" }),
    };

    public ControllerView PlayerView(PlayerId id) => new ButtonsView(null, [new Option("press", "+1"), new Option("move", "Move")]);
}
