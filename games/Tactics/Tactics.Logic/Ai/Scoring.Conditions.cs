using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>
    /// What a condition does to its holder (its modifiers and those it implies), split into fighting and holding up, and how
    /// many of the holder's turns each part can matter for: one that ends itself as the holder's turn starts (prone, Dodge)
    /// never touches how it fights and guards it for one round.
    /// </summary>
    private (double Offense, double Defense, double OffenseTurns, double DefenseTurns) ConditionBenefit(ConditionId id)
    {
        if (_conditions.TryGetValue(id, out var found))
            return found;
        var (offense, defense, offenseTurns, defenseTurns) = (0.0, 0.0, 3.0, 3.0);
        var seen = new List<ConditionId>();
        for (ConditionId? next = id; next is { } c && !seen.Contains(c) && Rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
        {
            seen.Add(c);
            var statics = new List<StaticMods>();
            if (def.Static is { } own)
                statics.Add(own);
            foreach (var passive in def.Passives.IsDefault ? [] : def.Passives)
            {
                if (passive.Static is { } passiveMods)
                    statics.Add(passiveMods);
                var effects = passive.Effects.IsDefault ? [] : passive.Effects;
                if (passive.Hook == HookKind.OnTurnStart && effects.Any(e => e is DamageEffect))
                    defense -= 0.3;
                if (passive.Hook == HookKind.OnTurnStart && effects.Any(e => e is RemoveEffect r && r.Condition == id))
                    (offenseTurns, defenseTurns) = (0, 1);
            }
            foreach (var mods in statics)
            {
                var (o, d) = ModsBenefit(mods);
                offense += o;
                defense += d;
            }
        }
        return _conditions[id] = (offense, defense, offenseTurns, defenseTurns);
    }

    /// <summary>
    /// What static modifiers do to their holder, in turns' worth: how much better it fights (an incapacitated unit loses its
    /// turn: −1) and how much better it holds up (attacks against it with advantage: −0.3).
    /// </summary>
    public static (double Offense, double Defense) ModsBenefit(StaticMods m)
    {
        static double EdgeWorth(Edge edge, double worth) => edge switch
        {
            Edge.Advantage => worth,
            Edge.Disadvantage => -worth,
            _ => 0,
        };

        var offense = (m.Incapacitated ? -1.0 : 0) + (m.CantMove ? -0.3 : 0) + EdgeWorth(m.OwnAttacks, 0.3)
                      + 0.05 * m.Attack + 0.04 * m.Damage + 0.05 * m.SpellDc + 0.8 * m.ExtraActions + 0.04 * m.Move + (m.Invisible ? 0.3 : 0);
        var defense = 0.06 * m.Ac - EdgeWorth(m.AttackedMelee, 0.3) - EdgeWorth(m.AttackedRanged, 0.15) + (m.Invisible ? 0.3 : 0)
                      - 0.1 * (m.AutoFailSaves?.Count ?? 0) - (m.CritWhenAdjacent ? 0.4 : 0)
                      + 0.01 * (m.Saves?.Values.Sum() ?? 0)
                      + 0.03 * (m.SaveEdges?.Values.Sum(e => EdgeWorth(e, 1)) ?? 0)
                      + 0.05 * ((m.Resist?.Count ?? 0) + (m.Immune?.Count ?? 0)) + (m.Evasion ? 0.05 : 0)
                      + 0.02 * (m.SaveProficiencies?.Count ?? 0);
        return (offense, defense);
    }
}
