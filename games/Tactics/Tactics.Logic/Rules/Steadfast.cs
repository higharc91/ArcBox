using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The boss guard (#106). A <em>guarded</em> unit is one whose monster stat block sets <c>"steadfast": true</c> (the sheet then
/// carries <see cref="Tag"/>), or that has a phase: any <c>hpBelow</c> trigger with a <c>phase</c> action on one of its tags.
/// Everyone else, heroes and ordinary enemies, keeps the plain rule: forced into a deep tile, a non-party unit dies.
/// <list type="bullet">
/// <item>A forced move that would put a guarded unit on a deep tile stops on the last tile of its path that isn't deep, or
/// doesn't move at all when there is none (<see cref="PushResisted"/>, see <see cref="Motion.Push"/>).</item>
/// <item>Damage that would take a unit past an untriggered phase threshold leaves it at the threshold, so the phase fires
/// (<see cref="PhaseFloor"/>, see <see cref="Hp.Damage"/>).</item>
/// </list>
/// </summary>
public static class Steadfast
{
    /// <summary>The sheet tag a stat block with <c>"steadfast": true</c> gives its unit.</summary>
    public const string Tag = "steadfast";

    /// <summary>True when <paramref name="unit"/> is guarded against deep-tile pushes (see <see cref="Steadfast"/>).</summary>
    public static bool Guarded(BattleState state, UnitState unit)
        => (unit.TrueSheet ?? unit.Sheet).Tags.Contains(Tag) || Thresholds(state, unit, untriggeredOnly: false).Length > 0;

    /// <summary>
    /// The HP <paramref name="unit"/> keeps when damage meant to leave it at <paramref name="after"/>, from <paramref name="before"/>,
    /// would take it past an untriggered phase threshold: the highest such threshold's HP (the most HP that is still below that
    /// percentage), or <paramref name="before"/> if it is already below it. One hit therefore triggers at most one phase and the
    /// rest of its damage is lost. Otherwise <paramref name="after"/>, untouched.
    /// </summary>
    public static int PhaseFloor(BattleState state, UnitState unit, int before, int after)
    {
        if (after >= before || state.Triggers.IsDefaultOrEmpty)
            return after;
        var crossed = Thresholds(state, unit, untriggeredOnly: true).Where(t => after < t).DefaultIfEmpty(-1).Max();
        return crossed < 0 ? after : Math.Max(after, Math.Min(before, crossed));
    }

    /// <summary>The most HP <paramref name="unit"/> can have and be below each of its phase triggers' percentages.</summary>
    private static ImmutableArray<int> Thresholds(BattleState state, UnitState unit, bool untriggeredOnly)
    {
        if (state.Triggers.IsDefaultOrEmpty)
            return [];
        var builder = ImmutableArray.CreateBuilder<int>();
        foreach (var trigger in state.Triggers)
        {
            if (trigger.Do.IsDefaultOrEmpty || !trigger.Do.Any(a => a.Phase is not null)
                || untriggeredOnly && state.FiredTriggers.Contains(trigger.Id))
                continue;
            foreach (var watch in Watches(trigger.When))
                if (Objectives.HasTag(unit, watch.Tag))
                    builder.Add(Math.Max(1, (watch.Percent * unit.Sheet.MaxHp - 1) / 100));
        }
        return builder.ToImmutable();
    }

    private static IEnumerable<HpWatch> Watches(TriggerWhen when)
    {
        if (when.HpBelow is { } watch)
            yield return watch;
        foreach (var inner in (when.All.IsDefault ? [] : when.All).Concat(when.Any.IsDefault ? [] : when.Any))
            foreach (var found in Watches(inner))
                yield return found;
    }
}
