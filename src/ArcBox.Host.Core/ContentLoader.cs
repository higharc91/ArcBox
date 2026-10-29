using ArcBox.Sdk.Protocol;

namespace ArcBox.Host.Core;

/// <summary>Something happened to one pack of a <see cref="ContentLoad"/>; posted by <see cref="ContentLoader"/> from any thread.</summary>
/// <param name="Attempt">Which start of the load it belongs to; events of an earlier attempt are ignored.</param>
public abstract record ContentLoadEvent(string GameId, int Attempt, string PackId) : HostEvent;

/// <summary>A pack download moved on to <paramref name="Fraction"/> (0–1). Posted once per whole percent.</summary>
public sealed record ContentProgressEvent(string GameId, int Attempt, string PackId, double Fraction)
    : ContentLoadEvent(GameId, Attempt, PackId);

/// <summary>A pack is installed (or was already cached) at <paramref name="Root"/>.</summary>
public sealed record ContentPackReadyEvent(string GameId, int Attempt, string PackId, string Root)
    : ContentLoadEvent(GameId, Attempt, PackId);

/// <summary>A pack couldn't be loaded.</summary>
public sealed record ContentFailedEvent(string GameId, int Attempt, string PackId, Exception Error)
    : ContentLoadEvent(GameId, Attempt, PackId);

/// <summary>The cache call: <see cref="ContentCache.EnsureAsync"/> for one pack of a game, returning its local directory.</summary>
public delegate Task<string> EnsurePack(string gameId, PackRef pack, IProgress<double> progress, CancellationToken ct);

/// <summary>
/// Where one game's required content packs stand (spec §3.6): what the TV's loading card shows. Changed only through
/// <see cref="ContentLoader"/>, on the main thread.
/// </summary>
public sealed class ContentLoad
{
    private readonly Dictionary<string, double> _progress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _roots = new(StringComparer.Ordinal);

    internal ContentLoad(string gameId, IReadOnlyList<PackRef> packs)
    {
        GameId = gameId;
        Packs = packs.DistinctBy(p => p.PackId, StringComparer.Ordinal).ToList();
    }

    public string GameId { get; }

    public IReadOnlyList<PackRef> Packs { get; }

    /// <summary>The current start of this load (<see cref="ContentLoadEvent.Attempt"/>).</summary>
    public int Attempt { get; private set; }

    /// <summary>Every pack is installed: <see cref="Roots"/> has them all.</summary>
    public bool Ready => _roots.Count == Packs.Count;

    /// <summary>The first failure of the current attempt, or null.</summary>
    public Exception? Error { get; private set; }

    /// <summary>Each pack's local directory, by pack id, once it's installed.</summary>
    public IReadOnlyDictionary<string, string> Roots => _roots;

    /// <summary>
    /// The average across packs, as a rounded percentage. A finished pack counts as 100 % whatever it reported (a cache
    /// hit reports nothing), and the result stays at 99 until every pack is finished.
    /// </summary>
    public int Percent
    {
        get
        {
            if (Ready)
            {
                return 100;
            }
            var average = Packs.Average(p => _roots.ContainsKey(p.PackId) ? 1 : _progress.GetValueOrDefault(p.PackId));
            return Math.Min(99, (int)Math.Round(average * 100, MidpointRounding.AwayFromZero));
        }
    }

    internal IReadOnlyList<PackRef> Pending => Packs.Where(p => !_roots.ContainsKey(p.PackId)).ToList();

    /// <summary>The same set of packs and hashes, in any order.</summary>
    internal bool SameAs(IReadOnlyList<PackRef> packs) =>
        Packs.ToHashSet().SetEquals(packs.DistinctBy(p => p.PackId, StringComparer.Ordinal));

    internal void Begin(int attempt)
    {
        Attempt = attempt;
        Error = null;
        _progress.Clear();
    }

    internal bool Apply(ContentLoadEvent e)
    {
        if (e.Attempt != Attempt || _roots.ContainsKey(e.PackId) || Packs.All(p => p.PackId != e.PackId))
        {
            return false;
        }
        switch (e)
        {
            case ContentProgressEvent p:
                _progress[p.PackId] = Math.Clamp(p.Fraction, 0, 1);
                return true;
            case ContentPackReadyEvent r:
                _roots[r.PackId] = r.Root;
                return true;
            case ContentFailedEvent f when Error is null:
                Error = f.Error;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Gets each game's content packs into the cache (spec §3.6). <see cref="Require"/>, <see cref="Retry"/> and
/// <see cref="Apply"/> run on the main thread; downloads run wherever <paramref name="ensure"/> puts them and report
/// through <paramref name="post"/> (a <see cref="MainThreadQueue"/>), so a <see cref="ContentLoad"/> is only ever changed by
/// <see cref="Apply"/>. A pack whose <paramref name="ensure"/> task is already complete (a cache hit) is applied at once,
/// without waiting a frame.
/// </summary>
public sealed class ContentLoader(EnsurePack ensure, Action<HostEvent> post)
{
    private readonly Dictionary<string, (ContentLoad Load, CancellationTokenSource Cts)> _loads = new(StringComparer.Ordinal);
    private int _attempts;

    /// <summary>The game's load, or null when it needs no packs (or none were required yet).</summary>
    public ContentLoad? For(string gameId) => _loads.TryGetValue(gameId, out var entry) ? entry.Load : null;

    /// <summary>
    /// <c>ContentRequired</c>: starts loading the game's packs, unless the same packs are loading or loaded already (a
    /// failed load is tried again). An empty list forgets the game's load: there is nothing to wait for.
    /// </summary>
    public ContentLoad? Require(string gameId, IReadOnlyList<PackRef> packs)
    {
        if (_loads.TryGetValue(gameId, out var entry))
        {
            if (packs.Count > 0 && entry.Load.SameAs(packs))
            {
                if (entry.Load.Error is not null)
                {
                    Start(entry.Load);
                }
                return entry.Load;
            }
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            _loads.Remove(gameId);
        }

        if (packs.Count == 0)
        {
            return null;
        }
        var load = new ContentLoad(gameId, packs);
        Start(load);
        return load;
    }

    /// <summary>Tries the game's failed load again: <paramref name="ensure"/> runs for each pack not installed yet.</summary>
    public void Retry(string gameId)
    {
        if (For(gameId) is { Error: not null } load)
        {
            Start(load);
        }
    }

    /// <summary>Applies a posted event; returns the load it changed, or null when it was stale or for no load.</summary>
    public ContentLoad? Apply(ContentLoadEvent e) =>
        For(e.GameId) is { } load && load.Apply(e) ? load : null;

    /// <summary>Forgets every load and cancels its downloads (a new server, or the app closing); their late events are ignored.</summary>
    public void Clear()
    {
        foreach (var (_, cts) in _loads.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _loads.Clear();
    }

    private void Start(ContentLoad load)
    {
        if (_loads.TryGetValue(load.GameId, out var previous))
        {
            previous.Cts.Cancel();
            previous.Cts.Dispose();
        }
        var cts = new CancellationTokenSource();
        _loads[load.GameId] = (load, cts);

        var attempt = ++_attempts;
        load.Begin(attempt);
        foreach (var pack in load.Pending)
        {
            Task<string> task;
            try
            {
                task = ensure(load.GameId, pack, new PackProgress(post, load.GameId, attempt, pack.PackId), cts.Token);
            }
            catch (Exception ex)
            {
                task = Task.FromException<string>(ex);
            }

            if (task.IsCompleted)
            {
                load.Apply(Outcome(load.GameId, attempt, pack.PackId, task));
            }
            else
            {
                _ = ObserveAsync(load.GameId, attempt, pack.PackId, task);
            }
        }
    }

    private async Task ObserveAsync(string gameId, int attempt, string packId, Task<string> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Reported by Outcome.
        }
        post(Outcome(gameId, attempt, packId, task));
    }

    private static ContentLoadEvent Outcome(string gameId, int attempt, string packId, Task<string> task) =>
        task.IsCompletedSuccessfully
            ? new ContentPackReadyEvent(gameId, attempt, packId, task.Result)
            : new ContentFailedEvent(gameId, attempt, packId,
                task.Exception?.InnerException ?? new OperationCanceledException($"Loading content pack '{packId}' was cancelled."));

    /// <summary>Posts a pack's progress whenever it reaches a new whole percent.</summary>
    private sealed class PackProgress(Action<HostEvent> post, string gameId, int attempt, string packId) : IProgress<double>
    {
        private int _lastPercent = -1;

        public void Report(double value)
        {
            var percent = (int)Math.Floor(Math.Clamp(value, 0, 1) * 100);
            if (Interlocked.Exchange(ref _lastPercent, percent) != percent)
            {
                post(new ContentProgressEvent(gameId, attempt, packId, value));
            }
        }
    }
}
