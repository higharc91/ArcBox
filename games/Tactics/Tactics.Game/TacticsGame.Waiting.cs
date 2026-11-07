using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    /// <summary>Read-only descriptions, independent of turn economy, range or previously cached phone views.</summary>
    private ImmutableArray<PhoneAbility> WaitingAbilities(UnitState unit)
    {
        var itemAbilities = Rules.Items.Values.Select(i => i.Use).ToHashSet();
        var abilities = unit.Sheet.Abilities.Concat(Rules.Abilities.Keys.Where(a => a.Value.StartsWith("common.", StringComparison.Ordinal)))
            .Distinct().Order().Where(a => a != Battle.UseItem && !itemAbilities.Contains(a)).Where(Rules.Abilities.ContainsKey)
            .Select(a => new PhoneAbility(a, null, Rules.Abilities[a].Name,
                Tooltips.For(Rules.Abilities[a], unit.Sheet.Level, Rules, State.Environment), Rules.Abilities[a].Cost, [], []));
        var items = unit.Bag.Where(i => i.Count > 0).DistinctBy(i => i.Item).OrderBy(i => i.Item)
            .Where(i => Rules.Items.ContainsKey(i.Item)).Select(i => Rules.Items[i.Item]).Where(i => Rules.Abilities.ContainsKey(i.Use))
            .Select(i => new PhoneAbility(Battle.UseItem, i.Id, i.Name,
                Tooltips.For(Rules.Abilities[i.Use], unit.Sheet.Level, Rules, State.Environment), Rules.Abilities[i.Use].Cost, [], []));
        return [.. abilities, .. items];
    }

    /// <summary>The last three public, completed battle events, in chronological order; never queued future steps.</summary>
    private ImmutableArray<string> RecentBattleEvents()
    {
        string Name(UnitId id) => State.Units.GetValueOrDefault(id)?.Sheet.Name ?? id.Value;
        string? Words(BattleEvent e) => e switch
        {
            Damaged d => $"{Name(d.Unit)} took {d.Amount} damage.",
            Healed h => $"{Name(h.Unit)} regained {h.Amount} HP.",
            AttackResolved { Hit: false } a => $"{Name(a.Source)} missed {Name(a.Target)}.",
            Downed d => $"{Name(d.Unit)} is downed.",
            Revived r => $"{Name(r.Unit)} revived with {r.Hp} HP.",
            UnitOut u => $"{Name(u.Unit)} is out of the battle.",
            Countered c => $"{Name(c.By)} countered {Name(c.Caster)}'s {Rules.Abilities.GetValueOrDefault(c.Ability)?.Name ?? c.Ability.Value}.",
            AbilityUsed a => $"{Name(a.Source)} used {Rules.Abilities.GetValueOrDefault(a.Ability)?.Name ?? a.Ability.Value}.",
            RoundStarted r => $"Round {r.Round} began.",
            _ => null,
        };
        return [.. Save_.Log.Reverse().SelectMany(b => b.Events.Reverse()).Select(Words).OfType<string>().Take(3).Reverse()];
    }
}
