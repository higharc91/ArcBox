namespace ArcBox.Host.Core.Audio;

/// <summary>How a line competes for the voice: a question always wins over commentary.</summary>
public enum NarrationPriority
{
    /// <summary>A commentator line: waits its turn and is dropped when it has waited too long.</summary>
    Commentary = 0,

    /// <summary>A question being read out: cuts off whatever is playing and goes to the front.</summary>
    Question = 1,
}

/// <summary>One line to speak, queued at <paramref name="QueuedAt"/> seconds on the narrator's clock.</summary>
public sealed record Narration(string Text, string Theme, NarrationPriority Priority, double QueuedAt);

/// <summary>
/// The narrator's one-voice queue (audio spec §3.3): one line at a time; a question preempts commentary (and an older
/// question) and goes ahead of everything waiting; commentary that has waited more than <see cref="StaleSeconds"/>
/// is dropped instead of being said late. Never talks over the question: commentary queued while it plays just waits.
/// Single-threaded (the narrator calls it on the main thread); times are seconds on any monotonic clock.
/// </summary>
public sealed class NarrationQueue
{
    /// <summary>Commentary older than this when its turn comes is dropped.</summary>
    public const double StaleSeconds = 4;

    private readonly List<Narration> _pending = [];

    /// <summary>The line being spoken (or fetched), until <see cref="Finish"/>.</summary>
    public Narration? Current { get; private set; }

    public IReadOnlyList<Narration> Pending => _pending;

    /// <summary>Queues <paramref name="line"/>; true when <see cref="Current"/> must be cut off now (a question arrived).</summary>
    public bool Enqueue(Narration line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Priority != NarrationPriority.Question)
        {
            _pending.Add(line);
            return false;
        }
        // Only the newest question matters; it goes ahead of any commentary still waiting.
        _pending.RemoveAll(p => p.Priority == NarrationPriority.Question);
        _pending.Insert(0, line);
        return Current is not null;
    }

    /// <summary>
    /// Starts the next line at <paramref name="now"/> and returns it, or null while one is still playing or nothing is
    /// waiting. Stale commentary is skipped (and added to <paramref name="dropped"/>).
    /// </summary>
    public Narration? Next(double now, List<Narration>? dropped = null)
    {
        if (Current is not null)
        {
            return null;
        }
        while (_pending.Count > 0)
        {
            var line = _pending[0];
            _pending.RemoveAt(0);
            if (line.Priority == NarrationPriority.Commentary && now - line.QueuedAt > StaleSeconds)
            {
                dropped?.Add(line);
                continue;
            }
            Current = line;
            return line;
        }
        return null;
    }

    /// <summary>The current line has finished (or was cut off).</summary>
    public void Finish() => Current = null;

    public void Clear()
    {
        _pending.Clear();
        Current = null;
    }
}
