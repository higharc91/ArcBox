using System.Text.Json;
using ArcBox.Sdk.Testing;

namespace Meridian.Game.Tests;

/// <summary>A Meridian game on the platform's test harness, with the shipped pack.</summary>
internal sealed class Harness
{
    public GameHarness<MeridianGame> H { get; }
    public MeridianGame Game => H.Game;
    public MeridianSession Session => H.Game.Session;
    public GameState State => Session.Game!;
    public RulesPack Pack => Game.Pack;

    /// <param name="scenario">Forces the scenario every Breach picks, as the Development setting does.</param>
    public Harness(int players = 3, int seed = 1, string? scenario = null)
    {
        var pack = scenario is null ? BasePack.Pack : BasePack.Pack.WithForcedScenario(scenario);
        H = new GameHarness<MeridianGame>(new MeridianGame(new Lazy<RulesPack>(() => pack)), players, seed);
        H.Start();
    }

    public void Send(int player, string action, object? data = null)
        => H.Input(player, new CustomInput(action, data is null ? null : JsonSerializer.SerializeToElement(data, ArcJson.Options)));

    /// <summary>The player's screen name, or "wait" for a WaitView.</summary>
    public string Screen(int player) => H.PlayerView(player) switch
    {
        CustomView c => c.Screen,
        WaitView => "wait",
        var v => v.GetType().Name,
    };

    public T Data<T>(int player) => ((CustomView)H.PlayerView(player)).Data.Deserialize<T>(ArcJson.Options)!;

    public SeatId Seat(int player) => new(H.Players[player].Id.Value);

    public int Index(SeatId seat) => H.Players.Select((p, i) => (p, i)).First(x => x.p.Id.Value == seat.Value).i;

    /// <summary>Every seat picks the crew member in its own position in the roster.</summary>
    public Harness Board()
    {
        for (var i = 0; i < H.Players.Count; i++)
        {
            Send(i, "pick", new { crew = Pack.Crew[i].Id.Value });
        }
        return this;
    }

    public void Advance(double seconds) => H.Advance(TimeSpan.FromSeconds(seconds));

    /// <summary>Plays bots until something is being presented. Returns false if the game ended first.</summary>
    public bool PlayUntilPresenting(ulong seed = 5)
    {
        var rng = Rng.Seeded(seed, 99);
        for (var i = 0; i < 2000 && Session.Phase == SessionPhase.Explore; i++)
        {
            if (!Session.Queue.IsEmpty)
            {
                return true;
            }
            var seat = BotRunner.Actor(State);
            Do(ExploreBot.Instance.Choose(Pack, State, seat, Rules.Legal(Pack, State, seat), ref rng));
        }
        return false;
    }

    public void Presented(int seq) => H.HostSignal(MeridianGame.PresentedSignal, new { seq });

    /// <summary>The index of the player the game is waiting on.</summary>
    public int Actor => Index(BotRunner.Actor(State));

    /// <summary>Sends a command as the phone intent it stands for.</summary>
    public void Do(Command command)
    {
        var (action, data) = Intents.FromCommand(command);
        Send(Index(command.From), action, data);
    }

    /// <summary>
    /// Plays the whole game through the phones' own intents, as bots: the Explore bot, then everyone Ready at the
    /// briefings, the scenario's role bots, and the VIP's Continue at the Debrief. Each presentation ends on its fallback
    /// (no TV), and a bot that would wait out a deadline lets the clock run. Stops when the game is over or
    /// <paramref name="steps"/> run out; returns the steps taken.
    /// </summary>
    public int PlayBots(int steps = 4000, ulong seed = 5) => PlayUntil(() => false, steps, seed);

    /// <summary>Plays as <see cref="PlayBots"/> until <paramref name="stop"/> holds. Returns the steps taken.</summary>
    public int PlayUntil(Func<bool> stop, int steps = 4000, ulong seed = 5)
    {
        var rng = Rng.Seeded(seed, 99);
        var taken = 0;
        while (taken < steps && !stop() && Session.Phase is not (SessionPhase.Over or SessionPhase.Boarding))
        {
            taken++;
            if (!Session.Queue.IsEmpty)
            {
                Advance(Session.Queue[0].Seconds);
                continue;
            }
            switch (Session.Phase)
            {
                case SessionPhase.Briefing:
                    for (var i = 0; i < H.Players.Count; i++)
                    {
                        Send(i, "ready");
                    }
                    continue;
                case SessionPhase.Debrief:
                    Send(0, "continue");   // the harness's first player is the VIP
                    continue;
            }
            var seat = State.Waiting is VotePending vote ? vote.Owed[0] : BotRunner.Actor(State);
            IBotPolicy bot = State.Phase == Phase.Endgame && State.RoleOf(seat) is { } role
                ? Pack.Scenarios.Get(State.Board!.Scenario).Bot(role.Role)
                : ExploreBot.Instance;
            var command = bot.Choose(Pack, Visibility.For(State, seat), seat, Rules.Legal(Pack, State, seat), ref rng);
            if (command is Meridian.Logic.Timeout)
            {
                Advance(Math.Max(0.1, (Game.Deadline!.Value - H.Now).TotalSeconds));   // the phone sits idle
                continue;
            }
            Do(command);
        }
        return taken;
    }
}
