namespace Meridian.Logic;

/// <summary>The faces a roll showed. <see cref="Total"/> is what checks compare.</summary>
public sealed record DiceRoll(EqArray<int> Faces)
{
    public int Total => Faces.Sum();
}

/// <summary>Signal dice (spec §2.2): six-sided, with the faces in <see cref="MeridianRules.DieFaces"/>.</summary>
public static class SignalDice
{
    /// <summary>Rolls <paramref name="count"/> dice. Zero (or fewer) dice is an empty roll totalling 0.</summary>
    public static (DiceRoll Roll, Rng Next) Roll(Rng rng, int count, MeridianRules rules)
    {
        var faces = new int[Math.Max(0, count)];
        for (var i = 0; i < faces.Length; i++)
        {
            (var index, rng) = rng.Below(rules.DieFaces.Count);
            faces[i] = rules.DieFaces[index];
        }
        return (new DiceRoll([.. faces]), rng);
    }
}
