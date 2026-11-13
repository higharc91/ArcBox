using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

/// <summary>
/// The platform adapter (spec §5.1). It owns everything with a clock, a connection or a view. Every change to the game
/// itself goes through <see cref="Rules.Step"/>; the adapter only translates phone input and timers into commands, and
/// events into views. All of its own state is <see cref="Session"/>, reassigned, never mutated.
/// </summary>
public sealed partial class MeridianGame(Lazy<RulesPack> pack) : IGame, IHostAware
{
    public const string Id = "meridian";

    private IGameContext _ctx = null!;
    private RulesPack _pack = null!;

    public GameManifest Manifest { get; } = new(Id, "Derelict: ISV Meridian",
        "Explore a silent starship, one compartment at a time. Something aboard is waiting.", MinPlayers: 3, MaxPlayers: 6);

    /// <summary>The adapter's whole state. Internal so tests can read it; nothing outside the class assigns it.</summary>
    internal MeridianSession Session { get; private set; } = null!;

    internal RulesPack Pack => _pack;

    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
    {
        _ctx = ctx;
        _pack = pack.Value;
        var seed = (ulong)ctx.Random.NextInt64();
        Session = new MeridianSession(
            SessionPhase.Boarding,
            players.Select(p => new SeatInfo(p.Id, p.Name)).ToEqArray(),
            seed,
            Game: null,
            BotRng: Rng.Seeded(seed, stream: 1013),
            new Clock(0, ClockPurpose.None),
            Queue: [],
            NextPresent: 1,
            Log: []);
        Rearm();
    }

    public void OnInput(PlayerId from, PlayerInput input)
    {
        if (Session.Find(from) is not { } seat || !Intents.TryRead(input, out var action, out var data))
        {
            return;
        }
        switch (Session.Phase)
        {
            case SessionPhase.Boarding:
                Board(seat, action, data);
                break;
            case SessionPhase.Explore when Intents.ToCommand(seat.Seat, action, data) is { } command:
                Apply(command);
                break;
        }
    }

    public void OnTimer(string key)
    {
        if (!Timing.TryParse(key, out var seq) || seq != Session.Clock.Seq)
        {
            return;   // a timer for a wait that has already ended (M2 review focus 1)
        }
        switch (Session.Clock.Purpose)
        {
            case ClockPurpose.Boarding:
                FinishBoarding();
                break;
            case ClockPurpose.Deadline:
                Apply(new Meridian.Logic.Timeout(BotRunner.Actor(Session.Game!)));
                break;
        }
    }

    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        if (Session.Find(id) is { } seat)
        {
            Session = Session.With(seat with { Connected = connected, Auto = connected ? false : seat.Auto });
            Rearm();
        }
    }

    public void OnHostSignal(string kind, PlayerInput payload)
    {
    }

    public object HostView() => Projection.Host(this);

    public ControllerView PlayerView(PlayerId id) => Projection.Player(this, id);

    /// <summary>When the current clock runs out, or null with no clock.</summary>
    internal DateTimeOffset? Deadline => _ctx.TimerDeadline(Timing.Key(Session.Clock.Seq));
}
