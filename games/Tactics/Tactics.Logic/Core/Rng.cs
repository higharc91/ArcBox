using System.Numerics;

namespace Tactics.Logic;

/// <summary>
/// A PCG32 generator (XSH RR output, O'Neill's reference constants) as an immutable value:
/// every draw returns the value and the next generator, so a state can be saved, replayed and compared.
/// </summary>
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

    /// <summary>The next 32 random bits.</summary>
    public (uint Value, Rng Next) NextUInt()
    {
        var old = State;
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (BitOperations.RotateRight(xorShifted, rot), Step());
    }

    /// <summary>An unbiased integer in 0..bound-1 (Lemire's method with rejection). <paramref name="bound"/> must be at least 1.</summary>
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

    /// <summary>A die roll in 1..sides.</summary>
    public (int Value, Rng Next) Roll(int sides)
    {
        var (value, next) = Below(sides);
        return (value + 1, next);
    }

    private Rng Step() => this with { State = State * Multiplier + Inc };
}
