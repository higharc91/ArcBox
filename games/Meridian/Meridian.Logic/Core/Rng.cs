using System.Numerics;

namespace Meridian.Logic;

/// <summary>
/// A PCG32 generator (XSH RR output, O'Neill's reference constants) as an immutable value: every draw returns the value
/// and the next generator. It lives inside <see cref="GameState"/>, so a saved state continues with the same draws.
/// </summary>
/// <remarks>The same algorithm as <c>Tactics.Logic.Rng</c>, copied rather than referenced: one game's rules must not
/// depend on another's.</remarks>
public readonly record struct Rng(ulong State, ulong Inc)
{
    private const ulong Multiplier = 6364136223846793005UL;

    /// <summary>Seeds like the reference <c>pcg32_srandom_r(seed, stream)</c>.</summary>
    public static Rng Seeded(ulong seed, ulong stream = 54)
    {
        var rng = new Rng(0, (stream << 1) | 1UL);
        rng = rng.Step();
        rng = rng with { State = rng.State + seed };
        return rng.Step();
    }

    public (uint Value, Rng Next) NextUInt()
    {
        var old = State;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (BitOperations.RotateRight(xorShifted, rot), Step());
    }

    /// <summary>An unbiased integer in 0..bound-1 (Lemire's method with rejection).</summary>
    public (int Value, Rng Next) Below(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
        var range = (uint)bound;
        var (x, rng) = NextUInt();
        var m = (ulong)x * range;
        var low = (uint)m;
        if (low < range)
        {
            var threshold = (0u - range) % range;
            while (low < threshold)
            {
                (x, rng) = rng.NextUInt();
                m = (ulong)x * range;
                low = (uint)m;
            }
        }
        return ((int)(m >> 32), rng);
    }

    /// <summary>A Fisher–Yates shuffle.</summary>
    public (EqArray<T> Items, Rng Next) Shuffle<T>(EqArray<T> items)
    {
        var array = items.Items.ToArray();
        var rng = this;
        for (var i = array.Length - 1; i > 0; i--)
        {
            (var j, rng) = rng.Below(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        return ([.. array], rng);
    }

    /// <summary>A generator for a separate purpose (a bot's choices), so drawing from it never moves this one.</summary>
    public Rng Fork(ulong stream)
    {
        var (a, next) = NextUInt();
        var (b, _) = next.NextUInt();
        return Seeded(((ulong)a << 32) | b, stream);
    }

    private Rng Step() => this with { State = State * Multiplier + Inc };
}
