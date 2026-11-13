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
}
