namespace Tactics.Logic;

/// <summary>
/// The minor injury an out-of-battle trap gives whoever sprang it (interactables design §1.6, USER DECISION 3, decided by the owner on
/// 2026-11-03): the trap's own <see cref="TrapDef.Injury"/> if the pack has it, else one picked by weight among the injuries that any long
/// rest mends (<c>"mends": "rest"</c>), in id order, as the battle table is picked. Pure: the caller rolls, and saves what it gave.
/// </summary>
public static class TrapInjuries
{
    /// <summary>The injuries a trap with no <see cref="TrapDef.Injury"/> picks from, in id order: every rest-kind injury with a weight.</summary>
    public static IReadOnlyList<InjuryDef> Table(RulesPack rules, IReadOnlyCollection<InjuryId>? held = null)
        => [.. rules.Injuries.Values.Where(i => i.Mends == "rest" && i.Weight > 0 && !(held?.Contains(i.Id) ?? false))];

    /// <summary>The sum of <see cref="Table"/>'s weights: the roll is from 1 to this. 0 when the pack has no rest-kind injury.</summary>
    public static int Total(RulesPack rules, IReadOnlyCollection<InjuryId>? held = null) => Table(rules, held).Sum(i => i.Weight);

    /// <summary>
    /// The injury <paramref name="trap"/> gives: its own, if the pack has it, else the one <paramref name="roll"/> (1 to <see cref="Total"/>)
    /// lands on. Null when the trap names none and the pack has no rest-kind injury (a pack of its own), so the trap does no harm.
    /// </summary>
    public static InjuryId? Pick(TrapDef trap, RulesPack rules, int roll, IReadOnlyCollection<InjuryId>? held = null)
    {
        if (trap.Injury is { } own && rules.Injuries.ContainsKey(new InjuryId(own)))
            return held?.Contains(new InjuryId(own)) == true ? null : new InjuryId(own);
        var table = Table(rules, held);
        if (table.Count == 0)
            return null;
        roll = Math.Clamp(roll, 1, table.Sum(i => i.Weight));
        foreach (var injury in table)
        {
            roll -= injury.Weight;
            if (roll <= 0)
                return injury.Id;
        }
        return table[^1].Id;
    }
}
