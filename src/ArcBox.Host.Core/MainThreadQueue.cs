using System.Collections.Concurrent;

namespace ArcBox.Host.Core;

/// <summary>
/// Hands <see cref="HostEvent"/>s from SignalR's pool threads to the game loop.
/// <see cref="Drain"/> is called once per frame and keeps only the latest <see cref="HostViewEvent"/>
/// (scenes only ever render the newest view), but every other event in order.
/// </summary>
public sealed class MainThreadQueue
{
    private readonly ConcurrentQueue<HostEvent> _queue = new();

    public void Post(HostEvent e) => _queue.Enqueue(e);

    public IReadOnlyList<HostEvent> Drain()
    {
        var all = new List<HostEvent>();
        while (_queue.TryDequeue(out var e))
        {
            all.Add(e);
        }

        var lastView = all.FindLastIndex(e => e is HostViewEvent);
        if (lastView < 0)
        {
            return all;
        }

        // The surviving view keeps the position of the last one, so it still precedes e.g. a GameEnded that followed it.
        var result = new List<HostEvent>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i] is not HostViewEvent || i == lastView)
            {
                result.Add(all[i]);
            }
        }
        return result;
    }
}
