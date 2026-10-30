namespace Tactics.Logic;

/// <summary>
/// Exact d20 and dice odds for the AI (spec §5.2). Floating point is fine here: the AI's numbers never reach the battle
/// state, and the same state always gives the same numbers.
/// </summary>
public static class Odds
{
    /// <summary>
    /// The chance that d20 + <paramref name="modifier"/> reaches <paramref name="ac"/>, where a natural 1 always misses and a
    /// natural 20 always hits; with advantage the better of two d20s counts, with disadvantage the worse.
    /// </summary>
    public static double Hit(int modifier, int ac, Edge edge)
    {
        var needed = Math.Clamp(ac - modifier, 2, 20);   // the lowest die from 2 to 19 that hits; 20 always does
        var single = (1 + Math.Max(0, 20 - needed)) / 20.0;
        return WithEdge(single, edge);
    }

    /// <summary>The chance of a natural 20 (a critical hit): 5%, 9.75% with advantage, 0.25% with disadvantage.</summary>
    public static double Crit(Edge edge) => WithEdge(0.05, edge);

    /// <summary>
    /// The chance that a saving throw of d20 + <paramref name="saveBonus"/> against <paramref name="dc"/> fails (saves have
    /// no natural 1 or 20 rule). Advantage is the saver's: it fails only when both dice do.
    /// </summary>
    public static double SaveFail(int saveBonus, int dc, Edge edge)
    {
        var success = Math.Clamp((21 - dc + saveBonus) / 20.0, 0, 1);
        return 1 - WithEdge(success, edge);
    }

    /// <summary>
    /// The expected damage of <paramref name="dice"/> from an attack that hits with chance <paramref name="pHit"/> (crits
    /// included) and crits with chance <paramref name="pCrit"/>: the average roll (never below 0) on a hit, and the dice
    /// once more on a crit.
    /// </summary>
    public static double ExpectedDamage(Dice dice, double pHit, double pCrit)
        => pHit * Math.Max(0, dice.Average) + pCrit * dice.Count * (dice.Sides + 1) / 2.0;

    /// <summary>The chance that at least one of two tries succeeds (advantage) or both do (disadvantage).</summary>
    private static double WithEdge(double single, Edge edge) => edge switch
    {
        Edge.Advantage => 1 - (1 - single) * (1 - single),
        Edge.Disadvantage => single * single,
        _ => single,
    };
}

/// <summary>
/// A discrete distribution of whole amounts (damage taken), as the probability of each value from <see cref="Min"/> up.
/// Immutable; the AI combines these to get the chance that a target drops.
/// </summary>
internal sealed class DamageDist
{
    private readonly double[] _p;

    private DamageDist(int min, double[] p)
    {
        Min = min;
        _p = p;
    }

    /// <summary>Always 0.</summary>
    public static DamageDist Zero { get; } = new(0, [1.0]);

    /// <summary>The smallest value with a probability.</summary>
    public int Min { get; }

    /// <summary>The largest value with a probability.</summary>
    public int Max => Min + _p.Length - 1;

    /// <summary>True when the amount is always 0.</summary>
    public bool IsZero => Min == 0 && _p.Length == 1;

    /// <summary>The mean.</summary>
    public double Mean
    {
        get
        {
            var mean = 0.0;
            for (var i = 0; i < _p.Length; i++)
                mean += _p[i] * (Min + i);
            return mean;
        }
    }

    /// <summary>A fixed amount.</summary>
    public static DamageDist Point(int value) => value == 0 ? Zero : new DamageDist(value, [1.0]);

    /// <summary>The sum of <paramref name="count"/> dice of <paramref name="sides"/> sides.</summary>
    public static DamageDist Dice(int count, int sides)
    {
        if (count <= 0 || sides <= 0)
            return Zero;
        var die = new double[sides];
        Array.Fill(die, 1.0 / sides);
        var one = new DamageDist(1, die);
        var total = one;
        for (var i = 1; i < count; i++)
            total = total.Plus(one);
        return total;
    }

    /// <summary>The distribution of the sum of an amount from this and an independent one from <paramref name="other"/>.</summary>
    public DamageDist Plus(DamageDist other)
    {
        if (other.IsZero)
            return this;
        if (IsZero)
            return other;
        var p = new double[_p.Length + other._p.Length - 1];
        for (var i = 0; i < _p.Length; i++)
        {
            if (_p[i] == 0)
                continue;
            for (var j = 0; j < other._p.Length; j++)
                p[i + j] += _p[i] * other._p[j];
        }
        return new DamageDist(Min + other.Min, p);
    }

    /// <summary>This shifted by a fixed amount.</summary>
    public DamageDist Shift(int amount) => amount == 0 ? this : new DamageDist(Min + amount, _p);

    /// <summary>Each value v becomes <paramref name="map"/>(v) (e.g. halved, or never below 0).</summary>
    public DamageDist Map(Func<int, int> map)
    {
        var byValue = new SortedDictionary<int, double>();
        for (var i = 0; i < _p.Length; i++)
        {
            if (_p[i] == 0)
                continue;
            var v = map(Min + i);
            byValue[v] = byValue.GetValueOrDefault(v) + _p[i];
        }
        return FromValues(byValue);
    }

    /// <summary>A weighted mixture: with probability w the amount comes from that distribution (the weights add up to 1).</summary>
    public static DamageDist Mix(IReadOnlyList<(double Weight, DamageDist Dist)> parts)
    {
        var live = parts.Where(p => p.Weight > 0).ToArray();
        if (live.Length == 0)
            return Zero;
        if (live.Length == 1)
            return live[0].Dist;
        var min = live.Min(p => p.Dist.Min);
        var max = live.Max(p => p.Dist.Max);
        var p = new double[max - min + 1];
        foreach (var (weight, dist) in live)
            for (var i = 0; i < dist._p.Length; i++)
                p[dist.Min - min + i] += weight * dist._p[i];
        return new DamageDist(min, p);
    }

    /// <summary>The chance that the amount is at least <paramref name="value"/>.</summary>
    public double AtLeast(int value)
    {
        var total = 0.0;
        for (var i = Math.Max(0, value - Min); i < _p.Length; i++)
            total += _p[i];
        return total;
    }

    /// <summary>The mean of the amount capped at <paramref name="cap"/>.</summary>
    public double MeanCapped(int cap)
    {
        var mean = 0.0;
        for (var i = 0; i < _p.Length; i++)
            mean += _p[i] * Math.Min(cap, Min + i);
        return mean;
    }

    private static DamageDist FromValues(SortedDictionary<int, double> byValue)
    {
        if (byValue.Count == 0)
            return Zero;
        var min = byValue.Keys.First();
        var p = new double[byValue.Keys.Last() - min + 1];
        foreach (var (v, w) in byValue)
            p[v - min] = w;
        return new DamageDist(min, p);
    }
}
