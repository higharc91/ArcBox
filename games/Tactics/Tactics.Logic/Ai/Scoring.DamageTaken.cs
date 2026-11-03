using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    private bool HasConsumableDamageTaken(UnitState target) => !target.Conditions.IsDefaultOrEmpty
        && Conditions.Defs(target, Rules).Any(c => c.DamageTaken?.Consume == true);

    private DamageDist DamageTaken(DamageDist dist, UnitState target, string type)
    {
        foreach (var def in Conditions.Defs(target, Rules))
            if (def.DamageTaken is { } trigger && trigger.Type == type)
                dist = dist.Map(v => v > 0 ? v + trigger.Bonus : v);
        return dist;
    }

    private sealed record CoatedPath(double Weight, Outcome Outcome, ImmutableSortedSet<ConditionId> Spent,
        ImmutableSortedSet<string> Fired);

    /// <summary>
    /// Keep damage and coating consumption in the same probability branch. A marginal "charge remaining" probability
    /// would get the mean right but could invent double-bonus kills. Merge only states with identical consumed/fired ids.
    /// </summary>
    private Outcome WalkDamageTaken(ImmutableArray<EffectDef> effects, Strike st, UnitState target, bool crit, bool half, RollResult? last)
    {
        void RestoreFired(ImmutableSortedSet<string> fired)
        {
            st.Fired.Clear(); st.Fired.UnionWith(fired);
        }
        ImmutableSortedSet<string> Fired() => st.Fired.ToImmutableSortedSet(StringComparer.Ordinal);
        UnitState Target(CoatedPath p) => target with { Conditions = target.Conditions.RemoveAll(c => p.Spent.Contains(c.Id)) };
        List<CoatedPath> Merge(IEnumerable<CoatedPath> paths) => [.. paths.Where(p => p.Weight > 0)
            .GroupBy(p => string.Join("\0", p.Spent.Select(c => c.Value)) + "\u0001" + string.Join("\0", p.Fired))
            .Select(g => g.First() with { Weight = g.Sum(p => p.Weight),
                Outcome = Outcome.Mix([.. g.Select(p => (p.Weight / g.Sum(q => q.Weight), p.Outcome))]) })];
        List<CoatedPath> Run(ImmutableArray<EffectDef> nodes, List<CoatedPath> paths, bool critical, bool halve, RollResult? roll)
        {
            foreach (var node in nodes.IsDefault ? [] : nodes)
                paths = Merge(paths.SelectMany(p => Step(node, p, critical, halve, roll)));
            return paths;
        }
        List<CoatedPath> Step(EffectDef effect, CoatedPath p, bool critical, bool halve, RollResult? roll)
        {
            RestoreFired(p.Fired);
            var current = Target(p);
            switch (effect)
            {
                case DamageEffect damage:
                {
                    var dist = Damage(damage, st, current, critical, halve);
                    var type = damage.Type ?? (damage.Weapon ? st.User.Sheet.Weapon.DamageType : null);
                    var used = Conditions.Active(current, Rules).Where(c => c.Def.DamageTaken is { Consume: true } trigger && trigger.Type == type)
                        .Select(c => c.From.Id).ToImmutableSortedSet();
                    if (used.IsEmpty || dist.AtLeast(1) <= 0)
                        return [p with { Outcome = p.Outcome.Then(new Outcome(dist)) }];
                    var positive = Math.Clamp(dist.AtLeast(1), 0, 1);
                    return [p with { Weight = p.Weight * (1 - positive) },
                        p with { Weight = p.Weight * positive, Spent = p.Spent.Union(used), Outcome = p.Outcome.Then(new Outcome(dist.Positive())) }];
                }
                case AttackEffect attack:
                {
                    var odds = AttackOdds(attack, st, current);
                    var hit = new RollResult([], 0, odds.Modifier, 0, odds.Ac, true, false, false);
                    var paths = new List<CoatedPath>();
                    foreach (var (weight, isHit, isCrit) in new[] { (odds.Crit, true, true), (odds.Hit - odds.Crit, true, false), (1 - odds.Hit, false, false) })
                    {
                        if (weight <= 0) continue;
                        RestoreFired(p.Fired);
                        var actual = hit with { Success = isHit, Crit = isCrit };
                        var hooks = isHit ? OnHitPassives(attack, st, current, odds.Edge, actual) : [];
                        var branch = isHit ? attack.OnHit.IsDefault ? [] : attack.OnHit : attack.OnMiss;
                        if (isCrit && !attack.OnCrit.IsDefaultOrEmpty) branch = branch.AddRange(attack.OnCrit);
                        var branchPaths = Run(branch, [p with { Weight = p.Weight * weight, Fired = Fired() }], isCrit, false, actual);
                        paths.AddRange(Run(hooks, branchPaths, isCrit, false, actual));
                    }
                    return paths;
                }
                case SaveEffect save:
                {
                    var odds = SaveOdds(save, st, current);
                    var failed = Run(save.OnFail, [p with { Weight = p.Weight * odds.Fail }], false, odds.Evades, odds.Failed);
                    var success = new List<CoatedPath> { p with { Weight = p.Weight * (1 - odds.Fail) } };
                    var saved = odds.Failed with { Success = true };
                    if (save.HalfOnSuccess && !odds.Evades)
                        success = Run([.. (save.OnFail.IsDefault ? [] : save.OnFail).OfType<DamageEffect>()], success, false, true, saved);
                    return [.. failed, .. Run(save.OnSuccess, success, false, false, saved)];
                }
                case RepeatEffect repeat:
                {
                    var paths = new List<CoatedPath> { p };
                    for (var n = 0; n < repeat.Times.At(st.Context.Level); n++) paths = Run(repeat.Effects, paths, critical, halve, roll);
                    return paths;
                }
                case RandomEffect random:
                {
                    var total = random.Table.IsDefault ? 0 : random.Table.Sum(e => Math.Max(0, e.Weight));
                    return total <= 0 ? [p] : [.. random.Table.Where(e => e.Weight > 0).SelectMany(e =>
                        Run(e.Effects, [p with { Weight = p.Weight * e.Weight / total }], critical, halve, roll))];
                }
                case IfEffect check:
                {
                    var w = new BattleWriter(st.W.State with { Units = st.W.State.Units.SetItem(current.Id, current) }, Rules);
                    var args = new HookArgs(st.User.Id, current.Id, roll, Tile: current.Pos, Ability: st.Ability?.Id);
                    return Run(Hooks.Holds(check.When, w, args, Rules) ? check.Then : check.Else, [p], critical, halve, roll);
                }
                case ChainEffect chain:
                    return [.. Run(chain.Effects, [p], critical, halve, roll).Select(next => next with
                    { Outcome = next.Outcome with { Splash = next.Outcome.Splash + (next.Outcome.Damage.Mean - p.Outcome.Damage.Mean) * chain.Times * 0.5 } })];
                default:
                {
                    var outcome = p.Outcome.Then(One(effect, st, current, critical, halve, roll));
                    var spent = effect is RemoveEffect remove ? p.Spent.Add(remove.Condition)
                        : effect is ApplyEffect apply ? p.Spent.Remove(apply.Condition) : p.Spent;
                    return [p with { Outcome = outcome, Spent = spent, Fired = Fired() }];
                }
            }
        }
        st.ProjectingDamageTaken = true;
        try
        {
            var paths = Run(effects, [new(1, Outcome.None, [], Fired())], crit, half, last);
            foreach (var p in paths) st.Fired.UnionWith(p.Fired);
            return Outcome.Mix([.. paths.Select(p => (p.Weight, p.Outcome))]);
        }
        finally { st.ProjectingDamageTaken = false; }
    }
}
