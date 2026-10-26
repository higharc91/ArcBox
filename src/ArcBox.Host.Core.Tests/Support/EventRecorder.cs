using System.Threading.Channels;

namespace ArcBox.Host.Core.Tests.Support;

/// <summary>Records every <see cref="HostClient.Event"/> and lets a test wait for one that matches.</summary>
public sealed class EventRecorder
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Channel<HostEvent> _inbox = Channel.CreateUnbounded<HostEvent>();
    private readonly List<HostEvent> _seen = [];

    public EventRecorder(HostClient client)
    {
        client.Event += e => _inbox.Writer.TryWrite(e);
    }

    /// <summary>Forget everything received so far.</summary>
    public void Clear()
    {
        while (_inbox.Reader.TryRead(out _))
        {
        }
        _seen.Clear();
    }

    /// <summary>Takes the first not-yet-taken event of type <typeparamref name="T"/> that matches.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? match = null) where T : HostEvent
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            var i = _seen.FindIndex(e => e is T t && (match is null || match(t)));
            if (i >= 0)
            {
                var hit = (T)_seen[i];
                _seen.RemoveAt(i);
                return hit;
            }

            try
            {
                _seen.Add(await _inbox.Reader.ReadAsync(cts.Token));
            }
            catch (OperationCanceledException)
            {
                var got = string.Join(", ", _seen.Select(e => e.ToString()));
                throw new TimeoutException($"No matching {typeof(T).Name} within {Timeout.TotalSeconds}s. Unclaimed: [{got}]");
            }
        }
    }
}
