using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>Abilities built in C# for tests.</summary>
public static class TestAbilities
{
    /// <summary>The spec §4.2 ability: a WIS spell attack, 4d6/5d6/6d6 radiant and Lit for 1 round, for 1 Devotion.</summary>
    public static readonly AbilityDef GuidingBolt = new(
        new AbilityId("cleric.guiding_bolt"), "Guiding Bolt", Tier: 1,
        new CostDef(Action: 1, Resource: Ordinal.Map(("devotion", 1))),
        new TargetDef(TargetKind.Enemy, Range: 6, Los: true, new AreaDef(AreaShape.Single)),
        [
            new AttackEffect("spell", Stat.Wis,
                OnHit: [new DamageEffect(Levels((1, "4d6"), (5, "5d6"), (9, "6d6")), "radiant", []), new ApplyEffect(new ConditionId("lit"), 1)],
                OnMiss: [], OnCrit: []),
        ],
        new PresentationDef("cast", "signature.radiant_bolt", "holy"), new AiHints(["damage", "single"]));

    /// <summary>A by-level dice table.</summary>
    public static ByLevel<Dice> Levels(params (int Level, string Dice)[] steps)
        => new(steps.ToImmutableSortedDictionary(s => s.Level, s => Dice.Parse(s.Dice)));

    /// <summary>The same dice at every level.</summary>
    public static ByLevel<Dice> Flat(string dice) => ByLevel<Dice>.Constant(Dice.Parse(dice));

    /// <summary>The values the given dice would roll next from <paramref name="rng"/>, in order.</summary>
    public static int[] Next(Rng rng, params int[] sides)
    {
        var values = new int[sides.Length];
        for (var i = 0; i < sides.Length; i++)
            (values[i], rng) = rng.Roll(sides[i]);
        return values;
    }
}
