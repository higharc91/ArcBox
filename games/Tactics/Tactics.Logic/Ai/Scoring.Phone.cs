using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    private string EffectNames(UnitState unit, Func<StaticMods, bool> applies)
    {
        static string Name(string id) => id.Split('.')[^1].Replace('_', ' ');
        var names = Conditions.Defs(unit, Rules).Where(c => c.Static is { } mods && applies(mods)
                || !c.Passives.IsDefault && c.Passives.Any(p => p.Static is { } m && applies(m))).Select(c => c.Name)
            .Concat(Modifiers.Passives(unit, Rules).Where(p => p.Static is { } m && applies(m)).Select(p => Name(p.Id)))
            .Concat(unit.Conditions.Where(c => c.Mods is { } m && applies(m)).Select(c => Name(c.Id.Value)))
            .Concat(ZoneRules.PassivesAt(State, unit).Where(p => p.Item1.Static is { } m && applies(m)).Select(p => Name(p.Item1.Id)))
            .Distinct().ToArray();
        return names.Length == 0 ? "temporary effects" : string.Join(", ", names);
    }

    /// <summary>The first attack/save gate, using the same situated odds as resolution; no RNG is consumed.</summary>
    public AimRoll? AimRoll(AbilityDef ability, Strike st, UnitState target)
    {
        var gate = EffectTree.All(ability.Effects).FirstOrDefault(e => e is AttackEffect or SaveEffect);
        if (gate is AttackEffect attack)
        {
            var odds = AttackOdds(attack, st, target);
            var ranged = attack.Kind == "weapon" ? st.User.Sheet.Weapon.Ranged : ability.Target.Range > 1;
            var reasons = new List<string>();
            var own = Sources(st.User.Id, st.User.Pos); var other = Sources(target.Id, target.Pos);
            if (own.Any(m => m.OwnAttacks != Edge.None)) reasons.Add("attacker: " + EffectNames(st.User, m => m.OwnAttacks != Edge.None));
            if (own.Any(m => m.Invisible)) reasons.Add("invisible attacker");
            if (other.Any(m => (ranged ? m.AttackedRanged : m.AttackedMelee) != Edge.None)) reasons.Add("target: " + EffectNames(target, m => (ranged ? m.AttackedRanged : m.AttackedMelee) != Edge.None));
            if (other.Any(m => m.Invisible)) reasons.Add("invisible target");
            if (!ranged && Modifiers.Flanked(State, st.User.Id, target.Id, Rules)) reasons.Add("flanking");
            if (MarkBlock.MarkedBy(target, st.User.Id)) reasons.Add("marked target");
            if (TauntBlock.TauntedAway(State, st.User.Id, target.Id)) reasons.Add("taunted away");
            if (st.User.Conditions.Any(c => c.Id == RulesPack.Helped.Id)) reasons.Add("Help");
            return new("hit", Math.Round(odds.Hit * 100, 1), odds.Modifier, odds.Ac, odds.Edge,
                string.Join(", ", reasons));
        }
        if (gate is SaveEffect save)
        {
            var odds = SaveOdds(save, st, target);
            var mods = Modifiers.Sum(Sources(target.Id, target.Pos));
            var stat = SaveBlock.StatFor(save, target.Sheet, mods);
            var reasons = new List<string>();
            if (Sources(target.Id, target.Pos).Any(m => m.SaveEdges?.GetValueOrDefault(stat) is Edge.Advantage or Edge.Disadvantage)) reasons.Add($"{stat} save: " + EffectNames(target, m => m.SaveEdges?.GetValueOrDefault(stat) is Edge.Advantage or Edge.Disadvantage));
            if (target.Conditions.Any(c => c.Id == RulesPack.Helped.Id)) reasons.Add("Help");
            if (mods.AutoFailSaves?.Contains(stat) == true) reasons.Add("automatic failure");
            if (target.Id != st.User.Id && target.Sheet.Side == st.User.Sheet.Side && Metamagic.Has(st.User, Metamagic.Careful)) reasons.Add("Careful Spell");
            return new("fail save", Math.Round(odds.Fail * 100, 1), odds.Failed.Modifier, odds.Failed.Vs, odds.Edge,
                string.Join(", ", reasons));
        }
        return new("automatic", 100, 0, 0, Edge.None, "no roll");
    }

    /// <summary>Possible reaction tiles and expected secondary conduction damage. Shares the AI's branch/save maths, never rolls.</summary>
    public (ImmutableArray<Tile> Spread, ImmutableArray<UnitPreview> Affected) ReactionPreview(Strike st, Tile aim, ImmutableArray<UnitId> direct)
    {
        if (!State.Environment || st.Ability is not { } ability)
            return (default, []);
        var footprint = Areas.Tiles(ability.Target.Area, st.From, aim, st.W.Terrain);
        var spread = new SortedSet<Tile>();
        var damage = new Dictionary<UnitId, List<(double Weight, DamageDist Dist)>>();
        foreach (var branch in ReactionBranches(st, direct).Where(b => b.Chance > 0))
        {
            var branchDamage = new Dictionary<UnitId, DamageDist>();
            var changed = new HashSet<Tile>();
            foreach (var rule in Rules.Environment.Reactions)
            {
                if (rule.Damage is null || !branch.Damage.Any(d => d.Type == rule.Damage))
                    continue;
                var available = footprint.Where(t => !changed.Contains(t)).ToImmutableArray();
                if (rule.Conduct is { } conduct)
                {
                    var tiles = Conducted(st.W, available, conduct.Reach);
                    var effects = branch.Damage.Where(d => d.Type == rule.Damage).Cast<EffectDef>().ToImmutableArray();
                    var secondary = new Strike(st.W, st.User, null, aim, neutral: false);
                    foreach (var unit in st.W.State.Units.Values.Where(u => u.Life == LifeState.Up && tiles.Contains(u.Pos) && !direct.Contains(u.Id)))
                    {
                        var outcome = Walk([new SaveEffect(conduct.Save, null, effects, [], conduct.Half)], secondary, unit);
                        branchDamage[unit.Id] = branchDamage.GetValueOrDefault(unit.Id, DamageDist.Zero).Plus(outcome.Damage);
                    }
                    spread.UnionWith(tiles);
                    changed.UnionWith(tiles);
                }
                else
                {
                    var tiles = available.Where(t => Surfaces.Matches(st.W, Rules.Environment, rule.Tiles, t)).Order().ToImmutableArray();
                    spread.UnionWith(tiles.Where(t => rule.Surface != "burning" || !Surfaces.Water(st.W, t) && !Surfaces.Has(st.W, t, "scorched")));
                    changed.UnionWith(tiles);
                }
            }
            foreach (var (id, dist) in branchDamage)
            {
                if (!damage.TryGetValue(id, out var parts)) damage.Add(id, parts = []);
                parts.Add((branch.Chance, dist));
            }
        }
        UnitPreview Preview(UnitId id, List<(double Weight, DamageDist Dist)> parts)
        {
            var dist = DamageDist.Mix([.. parts, (Math.Max(0, 1 - parts.Sum(p => p.Weight)), DamageDist.Zero)]);
            return new(id, Math.Round(dist.Mean, 1), 0,
                new("reaction damage", Math.Round(dist.AtLeast(1) * 100, 1), 0, 0, Edge.None, "secondary reaction; whole use, includes trigger chance"), dist.Min, dist.Max);
        }
        return (spread.Count == 0 ? default : [.. spread],
            [.. damage.OrderBy(d => d.Key).Select(d => Preview(d.Key, d.Value))]);
    }
}
