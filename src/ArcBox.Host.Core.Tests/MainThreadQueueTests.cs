using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core.Tests;

public class MainThreadQueueTests
{
    private static HostViewEvent View(int n) => new("counter", JsonDocument.Parse($"{{\"n\":{n}}}").RootElement.Clone());

    private static LobbyEvent Lobby(string code) => new(new LobbyState(code, [], null, [], false, false, null));

    [Fact]
    public void Queue_coalesces_host_views_per_frame()
    {
        var q = new MainThreadQueue();
        var lobby = Lobby("BCDF");
        var ended = new GameEndedEvent(new GameResults([]));

        q.Post(View(1));
        q.Post(lobby);
        q.Post(View(2));
        q.Post(View(3));
        q.Post(ended);

        var drained = q.Drain();

        Assert.Equal(3, drained.Count);
        Assert.Same(lobby, drained[0]);
        var view = Assert.IsType<HostViewEvent>(drained[1]);
        Assert.Equal(3, view.View.GetProperty("n").GetInt32());
        Assert.Same(ended, drained[2]);
    }

    [Fact]
    public void Drain_empties_the_queue()
    {
        var q = new MainThreadQueue();
        q.Post(View(1));

        Assert.Single(q.Drain());
        Assert.Empty(q.Drain());
    }

    [Fact]
    public void Other_events_keep_their_order()
    {
        var q = new MainThreadQueue();
        var events = new HostEvent[]
        {
            new ConnectionEvent(HostConnectionState.Connecting, null),
            new ConnectionEvent(HostConnectionState.Connected, null),
            Lobby("A"),
            Lobby("B"),
            new RoomClosedEvent("bye"),
        };
        foreach (var e in events)
        {
            q.Post(e);
        }

        Assert.Equal(events, q.Drain());
    }

    [Fact]
    public async Task Posting_from_many_threads_loses_nothing()
    {
        var q = new MainThreadQueue();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                q.Post(Lobby($"{t}-{i}"));
            }
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(4000, q.Drain().Count);
    }
}
