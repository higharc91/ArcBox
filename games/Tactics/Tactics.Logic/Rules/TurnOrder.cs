using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Initiative and the round's turn order. There are no initiative rolls, so the order is known in advance.</summary>
public static class TurnOrder
{
    /// <summary>
    /// 10 + DEX modifier + the sheet's initiative bonus + static initiative modifiers (hasted +5, slowed −5, features),
    /// −2 in heavy armour.
    /// </summary>
    public static int Initiative(BattleState s, UnitId unit, RulesPack rules)
    {
        var sheet = s.Units[unit].Sheet;
        var heavy = sheet.Armor == ArmorKind.Heavy ? -2 : 0;
        return 10 + Stats.Mod(sheet.Score(Stat.Dex)) + sheet.InitiativeBonus + Modifiers.For(s, unit, rules).Initiative + heavy;
    }

    /// <summary>
    /// Every Up or Downed unit by initiative, highest first. Ties go to player-controlled units, then higher DEX score,
    /// then ordinal <see cref="UnitId"/>. A summon whose summoner is in the order acts right after it (its summons in that
    /// same sort order, each followed by its own); one whose summoner has left takes its place by its own initiative.
    /// </summary>
    public static ImmutableArray<UnitId> Sort(BattleState s, RulesPack rules)
    {
        var sorted = s.Units.Values
            .Where(DownedRules.InBattle)
            .Select(u => (Unit: u, Initiative: Initiative(s, u.Id, rules)))
            .OrderByDescending(x => x.Initiative)
            .ThenBy(x => x.Unit.Controller is PlayerControlled ? 0 : 1)
            .ThenByDescending(x => x.Unit.Sheet.Score(Stat.Dex))
            .ThenBy(x => x.Unit.Id)
            .Select(x => x.Unit)
            .ToImmutableArray();
        var present = sorted.Select(u => u.Id).ToImmutableSortedSet();
        var order = ImmutableArray.CreateBuilder<UnitId>(sorted.Length);

        void Place(UnitId id)
        {
            order.Add(id);
            foreach (var summon in sorted.Where(u => u.SummonedBy == id && u.Id != id))
                Place(summon.Id);
        }

        foreach (var unit in sorted.Where(u => u.SummonedBy is not { } owner || owner == u.Id || !present.Contains(owner)))
            Place(unit.Id);
        return order.ToImmutable();
    }
}
