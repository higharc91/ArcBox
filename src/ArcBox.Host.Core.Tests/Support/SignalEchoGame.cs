using ArcBox.Sdk;

namespace ArcBox.Host.Core.Tests.Support;

/// <summary>The smallest <see cref="IHostAware"/> game: its host view is the host signals received so far, as <c>kind:payload</c>.</summary>
public sealed class SignalEchoGame : IGame, IHostAware
{
    public const string Id = "signal-echo";

    private readonly List<string> _signals = [];

    public GameManifest Manifest { get; } = new(Id, "Signal echo", "Test game that echoes host signals", 1, 8);

    public void Start(IGameContext ctx, IReadOnlyList<Player> players) { }

    public void OnInput(PlayerId from, PlayerInput input) { }

    public void OnTimer(string key) { }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected) { }

    public void OnHostSignal(string kind, PlayerInput payload) => _signals.Add($"{kind}:{payload.Payload.GetRawText()}");

    public object HostView() => new { Signals = _signals.ToArray() };

    public ControllerView PlayerView(PlayerId id) => new ButtonsView(null, [new Option("ping", "Ping")]);
}
