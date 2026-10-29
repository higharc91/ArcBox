using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Games.Redline;

/// <summary>
/// The synchronous <see cref="IQuestionSource"/> the game uses (spec §4.5): questions from the OpenTDB pool first,
/// with any shortfall filled from the embedded fallback pack.
/// </summary>
/// <remarks>
/// Rooms call in from different threads. The pool hands out each question once (atomic dequeue); the fallback
/// cursor and its shuffled order are guarded by <see cref="_gate"/>. The shuffle uses a <see cref="Random"/> owned
/// by this source: it is server code, not game logic, so it does not need the game's deterministic randomness.
/// </remarks>
public sealed class PooledQuestionSource(
    OpenTdbQuestionPool pool,
    IOptions<OpenTdbOptions> options,
    ILogger<PooledQuestionSource> log) : IQuestionSource
{
    private readonly bool _enabled = options.Value.Enabled;
    private readonly Random _random = new();
    private readonly object _gate = new();
    private readonly FallbackCursor _allCursor = new(FallbackQuestions.All);
    private readonly Dictionary<Difficulty, FallbackCursor> _byDifficulty = [];

    public IReadOnlyList<TriviaQuestion> Take(int count) => TakeCore(count, null);

    /// <summary>
    /// Only questions of <paramref name="difficulty"/>: from the pool first, then from that difficulty's slice of the
    /// fallback pack, which has its own shuffled cursor with the same wrap-around and no-repeat guarantees.
    /// </summary>
    public IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty) => TakeCore(count, difficulty);

    private IReadOnlyList<TriviaQuestion> TakeCore(int count, Difficulty? difficulty)
    {
        if (count <= 0)
            return [];

        // Cap the initial capacity so an absurd count cannot fail on the up-front allocation.
        var result = new List<TriviaQuestion>(Math.Min(count, 1024));
        if (_enabled)
        {
            try
            {
                if (difficulty is { } d)
                    pool.TryTake(count, result, d);
                else
                    pool.TryTake(count, result);
            }
            catch (Exception e)
            {
                // The contract is "never throws"; fall back for whatever the pool did not hand over.
                log.LogWarning(e, "Taking questions from the OpenTDB pool failed");
            }
        }

        var shortfall = count - result.Count;
        if (shortfall > 0)
        {
            TakeFallback(shortfall, difficulty, result);
            log.LogWarning("Used {FallbackCount} fallback questions ({Requested} requested)", shortfall, count);
        }

        return result;
    }

    private void TakeFallback(int count, Difficulty? difficulty, List<TriviaQuestion> into)
    {
        lock (_gate)
        {
            var cursor = _allCursor;
            if (difficulty is { } d)
            {
                if (!_byDifficulty.TryGetValue(d, out var slice))
                {
                    var matches = FallbackQuestions.All.Where(q => q.Difficulty == d).ToList();
                    // An empty slice cannot be cycled; serve from the whole pack rather than throw.
                    slice = matches.Count > 0 ? new FallbackCursor(matches) : _allCursor;
                    _byDifficulty[d] = slice;
                }

                cursor = slice;
            }

            cursor.Take(count, into, _random);
        }
    }

    /// <summary>A shuffled walk over a fixed list of questions. Not thread-safe: always used under <c>_gate</c>.</summary>
    private sealed class FallbackCursor(IReadOnlyList<TriviaQuestion> pack)
    {
        private int[] _order = [];
        private int _cursor;

        public void Take(int count, List<TriviaQuestion> into, Random random)
        {
            var taken = new List<int>(Math.Min(count, pack.Count));
            for (var i = 0; i < count; i++)
            {
                if (_cursor >= _order.Length)
                {
                    Reshuffle(random);
                    // A reshuffle mid-call must not re-serve what this call already took from the old cycle's tail.
                    // Possible only when the call fits in the pack; a larger call repeats unavoidably.
                    if (i > 0 && count <= pack.Count)
                        MoveBeyondPrefix(taken, count - i);
                }

                var index = _order[_cursor++];
                into.Add(pack[index]);
                taken.Add(index);
            }
        }

        // Swaps every index in `avoid` that sits in the first `prefix` slots of the fresh order with a slot past the
        // prefix that holds an index not in `avoid`. The order stays a permutation, so the cycle still covers the pack.
        // When |avoid| + prefix <= pack size there are always enough such slots past the prefix.
        private void MoveBeyondPrefix(List<int> avoid, int prefix)
        {
            var avoidSet = new HashSet<int>(avoid);
            var next = prefix;
            for (var p = 0; p < prefix; p++)
            {
                if (!avoidSet.Contains(_order[p]))
                    continue;
                while (avoidSet.Contains(_order[next]))
                    next++;
                (_order[p], _order[next]) = (_order[next], _order[p]);
                next++;
            }
        }

        // Fisher-Yates; called once per cycle through the pack.
        private void Reshuffle(Random random)
        {
            var size = pack.Count;
            var order = new int[size];
            for (var i = 0; i < size; i++)
                order[i] = i;
            for (var i = size - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            _order = order;
            _cursor = 0;
        }
    }
}
