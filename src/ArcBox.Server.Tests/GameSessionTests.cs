using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.TestGames;
using Counter.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArcBox.Server.Tests;

public class GameSessionTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly List<Action> _queue = [];
    private readonly PlayerId P1 = new("p1");
    private readonly Player[] _players =
    [
        new(new("p1"), "P1", true),
        new(new("p2"), "P2", false),
        new(new("p3"), "P3", false)
    ];

    private JsonElement Press => Json(new { pressed = "press" });

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private void Pump()
    {
        var items = _queue.ToList();
        _queue.Clear();
        items.ForEach(a => a());
    }

    private GameSession NewSession(IGame game)
    {
        return new GameSession(game, _players, time, 1, a => _queue.Add(a), NullLogger.Instance);
    }

    [Fact]
    public void Start_publishes_host_and_all_player_views_with_kind()
    {
        var s = NewSession(new CounterGame());
        s.Start();
        var u = s.TakeViewUpdates();
        Assert.NotNull(u.Host);
        Assert.Equal(3, u.Players.Count);
        Assert.Equal("buttons", u.Players[0].View.GetProperty("kind").GetString());
        Assert.Equal(u.Players[0].ViewId, u.Players[0].View.GetProperty("viewId").GetString());
    }

    [Fact]
    public void Unchanged_views_are_not_republished()
    {
        var s = NewSession(new CounterGame());
        s.Start();
        var first = s.TakeViewUpdates();
        Assert.True(s.Input(P1, first.Players[0].ViewId, Press));
        var u = s.TakeViewUpdates();
        Assert.NotNull(u.Host);
        Assert.Empty(u.Players);
    }

    [Fact]
    public void Stale_view_id_is_dropped()
    {
        var s = NewSession(new CounterGame());
        s.Start();
        s.TakeViewUpdates();
        Assert.False(s.Input(P1, "v999", Press));
        Assert.Null(s.TakeViewUpdates().Host);
    }

    [Fact]
    public void Throwing_game_ends_session_as_crashed()
    {
        var s = NewSession(new ThrowerGame());
        s.Start();
        var v = s.TakeViewUpdates().Players[0].ViewId;
        s.Input(P1, v, Json(new { pressed = "boom" }));
        Assert.True(s.IsOver);
        Assert.Equal("Game crashed", s.Results!.Message);
        Assert.Same(ViewUpdates.None, s.TakeViewUpdates());
    }

    [Fact]
    public void Throw_in_Start_is_contained()
    {
        ThrowerGame.ThrowOnStart = true;
        try
        {
            var s = NewSession(new ThrowerGame());
            s.Start();
            Assert.True(s.IsOver);
        }
        finally
        {
            ThrowerGame.ThrowOnStart = false;
        }
    }

    [Fact]
    public void Timer_fires_through_room_queue_and_raises_TimerFired()
    {
        var s = NewSession(new CounterGame());
        var fired = 0;
        s.TimerFired += () => fired++;
        s.Start();
        time.Advance(CounterGame.Duration);
        Assert.False(s.IsOver); // queued, not yet run
        Pump();
        Assert.True(s.IsOver);
        Assert.Equal(1, fired);
    }

    // Review Focus #2
    [Fact]
    public void Timer_after_dispose_or_end_never_calls_game()
    {
        var s = NewSession(new CounterGame());
        s.Start();
        time.Advance(CounterGame.Duration);
        s.Dispose();
        Pump();
        Assert.False(s.IsOver); // OnTimer never ran, so EndGame was never called
    }

    // Review Focus #3
    [Fact]
    public void EndGame_inside_Start_stops_further_calls()
    {
        var s = NewSession(new EndsImmediatelyGame());
        s.Start(); // test-local IGame whose Start calls ctx.EndGame(new([]))
        Assert.True(s.IsOver);
        Assert.Same(ViewUpdates.None, s.TakeViewUpdates());
        Assert.False(s.Input(P1, "v1", Press));
    }

    [Fact]
    public void BusySince_is_null_outside_game_calls()
    {
        var s = NewSession(new CounterGame());
        s.Start();
        Assert.Null(s.BusySince);
    }

    private sealed class EndsImmediatelyGame : IGame
    {
        public GameManifest Manifest { get; } = new("ends", "Ends", "", 1, 8);

        public void Start(IGameContext ctx, IReadOnlyList<Player> players)
        {
            ctx.EndGame(new GameResults([]));
        }

        public void OnInput(PlayerId from, PlayerInput input) => throw new InvalidOperationException("Should not be called");
        public void OnTimer(string key) => throw new InvalidOperationException("Should not be called");
        public void OnPlayerConnectionChanged(PlayerId id, bool connected) => throw new InvalidOperationException("Should not be called");
        public object HostView() => throw new InvalidOperationException("Should not be called");
        public ControllerView PlayerView(PlayerId id) => throw new InvalidOperationException("Should not be called");
    }
}
