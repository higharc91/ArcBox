using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>What a roll was for. Carried by <see cref="Rolled"/> so the TV and phone can label it.</summary>
public enum RollKind { Attack, Save, Contest, Check, Damage, Countdown }

/// <summary>
/// Advantage (roll two d20s, keep the higher), disadvantage (keep the lower) or neither. JSON: <c>"none"</c>,
/// <c>"advantage"</c>, <c>"disadvantage"</c>.
/// </summary>
public enum Edge
{
    /// <summary>One d20.</summary>
    [JsonStringEnumMemberName("none")] None,
    /// <summary>Two d20s, keep the higher.</summary>
    [JsonStringEnumMemberName("advantage")] Advantage,
    /// <summary>Two d20s, keep the lower.</summary>
    [JsonStringEnumMemberName("disadvantage")] Disadvantage,
}

/// <summary>
/// A d20 roll: every die rolled (two with an edge, in roll order, so the TV can strike one out), the die kept, the
/// modifier, the total, what it was rolled against and the outcome. <see cref="Crit"/> is a natural 20 that crits;
/// <see cref="Fumble"/> a natural 1 that misses whatever the total.
/// </summary>
public sealed record RollResult(ImmutableArray<int> D20s, int Kept, int Modifier, int Total, int Vs, bool Success,
    bool Crit, bool Fumble);

/// <summary>Dice rolls from the battle's RNG (spec §3.4). Callers emit the <see cref="Rolled"/> event.</summary>
public static class Rolls
{
    /// <summary>Any advantage and any disadvantage cancel out; neither stacks.</summary>
    public static Edge Combine(int advantages, int disadvantages)
        => (advantages > 0, disadvantages > 0) switch
        {
            (true, false) => Edge.Advantage,
            (false, true) => Edge.Disadvantage,
            _ => Edge.None,
        };

    /// <summary>
    /// d20 + <paramref name="modifier"/> against <paramref name="vs"/>: a success when the total reaches it. With
    /// <paramref name="critOn20"/> a natural 20 always succeeds and crits; with <paramref name="autoMissOn1"/> a natural 1
    /// always fails (a fumble).
    /// </summary>
    public static RollResult D20(BattleWriter w, int modifier, int vs, Edge edge, bool critOn20, bool autoMissOn1)
    {
        var (dice, kept) = RollD20(w, edge);
        var total = kept + modifier;
        var crit = critOn20 && kept == 20;
        var fumble = autoMissOn1 && kept == 1;
        var success = !fumble && (crit || total >= vs);
        return new RollResult(dice, kept, modifier, total, vs, success, crit, fumble);
    }

    /// <summary>
    /// A contest: the attacker rolls, then the defender, and the higher total wins; a tie goes to the defender. Each
    /// result's <see cref="RollResult.Vs"/> is the other side's total and <see cref="RollResult.Success"/> says who won.
    /// </summary>
    public static (RollResult Attacker, RollResult Defender) Contest(BattleWriter w, int attackerModifier, Edge attackerEdge,
        int defenderModifier, Edge defenderEdge)
    {
        var (attackerDice, attackerKept) = RollD20(w, attackerEdge);
        var (defenderDice, defenderKept) = RollD20(w, defenderEdge);
        var attackerTotal = attackerKept + attackerModifier;
        var defenderTotal = defenderKept + defenderModifier;
        var attackerWins = attackerTotal > defenderTotal;
        return (
            new RollResult(attackerDice, attackerKept, attackerModifier, attackerTotal, defenderTotal, attackerWins, false, false),
            new RollResult(defenderDice, defenderKept, defenderModifier, defenderTotal, attackerTotal, !attackerWins, false, false));
    }

    /// <summary>
    /// A saving throw by <paramref name="unit"/>: d20 + the stat's modifier + proficiency (if proficient) + static save
    /// modifiers + <paramref name="cover"/> (only for DEX saves) against <paramref name="dc"/>. A stat in
    /// <see cref="StaticMods.AutoFailSaves"/> fails whatever the roll (the d20 is still rolled and shown). A helped unit
    /// rolls with advantage and uses the help up (<see cref="HelpBlock.Consume"/>). Emits
    /// <see cref="Rolled"/>, fires <see cref="HookKind.OnSave"/> for the unit (with <paramref name="source"/> as the other
    /// unit) and returns the roll.
    /// </summary>
    public static RollResult Save(BattleWriter w, UnitId unit, Stat stat, int dc, RulesPack rules, int cover = 0,
        UnitId? source = null)
    {
        var sheet = w.Unit(unit).Sheet;
        var mods = Modifiers.For(w, unit, rules);
        var modifier = Stats.Mod(sheet.Score(stat))
                       + (sheet.SaveProficiencies.Contains(stat) ? Stats.Proficiency(sheet.Level) : 0)
                       + (mods.Saves?.GetValueOrDefault(stat) ?? 0)
                       + (stat == Stat.Dex ? cover : 0);

        var edge = HelpBlock.Consume(w, unit) ? Edge.Advantage : Edge.None;
        var roll = D20(w, modifier, dc, edge, critOn20: false, autoMissOn1: false);
        if (mods.AutoFailSaves?.Contains(stat) == true)
            roll = roll with { Success = false };
        w.Emit(new Rolled(unit, RollKind.Save, roll));
        Hooks.Fire(w, HookKind.OnSave, new HookArgs(unit, source, roll), rules);
        return roll;
    }

    /// <summary>Rolls <paramref name="dice"/>; a <paramref name="crit"/> rolls the dice twice but adds the bonus once. Never below 0.</summary>
    public static int Damage(BattleWriter w, Dice dice, bool crit)
    {
        var count = crit ? dice.Count * 2 : dice.Count;
        var total = dice.Bonus;
        for (var i = 0; i < count; i++)
            total += w.Roll(dice.Sides);
        return Math.Max(0, total);
    }

    private static (ImmutableArray<int> Dice, int Kept) RollD20(BattleWriter w, Edge edge)
    {
        var first = w.Roll(20);
        if (edge == Edge.None)
            return ([first], first);
        var second = w.Roll(20);
        var kept = edge == Edge.Advantage ? Math.Max(first, second) : Math.Min(first, second);
        return ([first, second], kept);
    }
}
