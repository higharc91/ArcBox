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

    public Harness(int players = 3, int seed = 1)
    {
        H = new GameHarness<MeridianGame>(new MeridianGame(new Lazy<RulesPack>(() => BasePack.Pack)), players, seed);
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
    /// Plays the Explore bot's choice for whoever the game waits on, through the phones' own intents, until the game is
    /// over or <paramref name="steps"/> run out. Returns the steps taken.
    /// </summary>
    public int PlayBots(int steps = 2000, ulong seed = 5)
    {
        var rng = Rng.Seeded(seed, 99);
        var taken = 0;
        while (taken < steps && Session.Phase == SessionPhase.Explore)
        {
            if (!Session.Queue.IsEmpty)
            {
                Advance(Session.Queue[0].Seconds);   // no TV: each presentation ends on its fallback
                continue;
            }
            var seat = BotRunner.Actor(State);
            Do(ExploreBot.Instance.Choose(Pack, State, seat, Rules.Legal(Pack, State, seat), ref rng));
            taken++;
        }
        return taken;
    }
}
