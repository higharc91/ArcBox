using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    // A plan's water geometry is independent of where its caster stands. Zone and sunk snapshots make
    // reuse safe if a caller supplies a different insulating/water state on its writer.
    private readonly Dictionary<(string Footprint, int Reach, ImmutableArray<Zone> Zones, ImmutableSortedSet<Tile>? Sunk), ImmutableArray<Tile>> _conducted = [];

    public bool Reacts(AbilityDef ability, UnitSheet user) => State.Environment && All(ability.Effects).OfType<DamageEffect>()
        .Any(d => Rules.Environment.Reactions.Any(r => r.Damage is { } type && type == (d.Type ?? (d.Weapon ? user.Weapon.DamageType : null))));

    public bool ChangesSurface(AbilityDef ability, UnitSheet user) => State.Environment && All(ability.Effects).OfType<DamageEffect>()
        .Any(d => Rules.Environment.Reactions.Any(r => (r.Surface is not null || r.Ends) && r.Damage is { } type
            && type == (d.Type ?? (d.Weapon ? user.Weapon.DamageType : null))));

    public ImmutableArray<Tile> Conducted(BattleWriter w, ImmutableArray<Tile> footprint, int reach)
    {
        var key = (string.Join(";", footprint.Order()), reach, w.State.Zones, w.State.Sunk);
        if (!_conducted.TryGetValue(key, out var tiles))
            _conducted[key] = tiles = Surfaces.Conducted(w, footprint, reach);
        return tiles;
    }

    /// <summary>Reaction value and any possible ally damage, without applying effects or drawing RNG.</summary>
    public (double Value, bool FriendlyDamage) ReactionWorth(Strike st, Tile aim, ImmutableArray<UnitId> direct, AiProfile profile)
    {
        if (!st.W.State.Environment || st.Ability is not { } ability)
            return (0, false);
        var footprint = Areas.Tiles(ability.Target.Area, st.From, aim, st.W.Terrain);
        var value = 0.0;
        var friendly = false;
        foreach (var branch in ReactionBranches(st, direct))
        {
            if (branch.Chance <= 0 || branch.Damage.IsEmpty)
                continue;
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
                    // Like ReactStep: a secondary save uses the caster's DC and has no spell-shaping modifiers.
                    var secondary = new Strike(st.W, st.User, null, aim, neutral: false);
                    foreach (var unit in st.W.State.Units.Values.Where(u => u.Life == LifeState.Up && tiles.Contains(u.Pos) && !direct.Contains(u.Id)))
                    {
                        var outcome = Walk([new SaveEffect(conduct.Save, null, effects, [], conduct.Half)], secondary, unit);
                        value += branch.Chance * Worth(st.User, unit, outcome, profile);
                        friendly |= !Hostile(st.User, unit) && unit.Sheet.Side != Side.Neutral && outcome.Damage.Mean > 0;
                    }
                    changed.UnionWith(tiles);
                }
                else
                {
                    var tiles = available.Where(t => Surfaces.Matches(st.W, Rules.Environment, rule.Tiles, t)).Order().ToImmutableArray();
                    if (rule.Surface is { } kind)
                    {
                        var safe = tiles.Where(t => kind != "burning" || !Surfaces.Water(st.W, t) && !Surfaces.Has(st.W, t, "scorched")).ToImmutableArray();
                        var worth = SurfaceWorth(st, safe, Rules.Environment.Surfaces[kind], rule, profile);
                        value += branch.Chance * worth.Value;
                        friendly |= worth.FriendlyDamage;
                        if (st.W.State.Difficulty == Difficulty.Story && Rules.Environment.Surfaces[kind].Spreads)
                            friendly |= SpreadHurtsAlly(st, safe, Rules.Environment.Surfaces[kind]);
                    }
                    changed.UnionWith(tiles);
                }
            }
        }
        return (value, friendly);
    }

    /// <summary>Story also protects allies farther along the deterministic fuel patch, before anyone moves.</summary>
    private bool SpreadHurtsAlly(Strike st, ImmutableArray<Tile> seeds, SurfaceDef surface)
    {
        var reached = seeds.ToHashSet();
        var pending = new Queue<Tile>(seeds);
        while (pending.TryDequeue(out var tile))
            foreach (var next in Surfaces.Neighbours(tile))
                if (!reached.Contains(next) && Surfaces.Fuel(st.W, Rules.Environment, next))
                {
                    reached.Add(next);
                    pending.Enqueue(next);
                }
        var secondary = new Strike(st.W, st.User, null, st.Aim, neutral: false);
        foreach (var unit in st.W.State.Units.Values.Where(u => u.Life == LifeState.Up && u.Sheet.Side != Side.Neutral
                     && !Hostile(st.User, u) && reached.Contains(u.Pos)))
            foreach (var passive in surface.Passives.IsDefault ? [] : surface.Passives)
                if (passive.Target == PassiveTarget.Self && passive.Hook == HookKind.OnTurnStart
                    && (passive.When is null || Hooks.Holds(passive.When, st.W, new HookArgs(unit.Id, Tile: unit.Pos), Rules))
                    && Walk(passive.Effects, secondary, unit).Damage.Mean > 0)
                    return true;
        return false;
    }

    private (double Value, bool FriendlyDamage) SurfaceWorth(Strike st, ImmutableArray<Tile> tiles, SurfaceDef surface,
        EnvironmentReactionDef rule, AiProfile profile)
    {
        if (tiles.IsEmpty)
            return (0, false);
        var rounds = Math.Min(3, rule.Rounds ?? surface.Rounds ?? 3); // existing zone-scoring horizon
        var passives = rule.Passives.IsDefaultOrEmpty ? surface.Passives : rule.Passives;
        var value = 0.0;
        var friendly = false;
        var secondary = new Strike(st.W, st.User, null, st.Aim, neutral: false);
        foreach (var unit in st.W.State.Units.Values.Where(u => u.Life == LifeState.Up))
        {
            var standing = tiles.Contains(unit.Pos);
            if (!standing && !tiles.Any(t => Surfaces.Neighbours(t).Contains(unit.Pos)))
                continue;
            // Shared enter/start ids are one damage opportunity, not two. Adjacent units may cross,
            // using the existing 0.5 enter weight; fixed-entry control (ice) is charged only once.
            foreach (var passive in (passives.IsDefault ? [] : passives).Where(p => p.Target == PassiveTarget.Self
                         && p.Hook is HookKind.OnTurnStart or HookKind.OnEnterTile).GroupBy(p => p.Id)
                         .Select(g => g.OrderByDescending(p => p.Hook == HookKind.OnTurnStart).First()))
            {
                var weight = passive.Hook == HookKind.OnTurnStart ? rounds * (standing ? 1 : 0.5) : 0.5;
                if (passive.When is { } when && !Hooks.Holds(when, st.W, new HookArgs(unit.Id, Tile: unit.Pos), Rules))
                    continue;
                var outcome = Walk(passive.Effects, secondary, unit);
                value += weight * Worth(st.User, unit, outcome, profile);
                friendly |= !Hostile(st.User, unit) && unit.Sheet.Side != Side.Neutral && outcome.Damage.Mean > 0;
            }
            if (surface.Difficult && standing && Hostile(st.User, unit))
                value += 0.3 * profile.Control;
        }
        return (value, friendly);
    }

    private sealed record ReactionBranch(double Chance, ImmutableArray<DamageEffect> Damage);

    /// <summary>Selected elements and hit odds. Saves do not reduce the full dice carried by water.</summary>
    private List<ReactionBranch> ReactionBranches(Strike st, ImmutableArray<UnitId> direct)
    {
        var ability = st.Ability!;
        var target = direct.IsEmpty ? null : st.W.Unit(direct[0]);
        List<ReactionBranch> Empty() => [new(1, [])];
        static List<ReactionBranch> Both(List<ReactionBranch> first, List<ReactionBranch> next)
            => [.. first.SelectMany(a => next.Select(b => new ReactionBranch(a.Chance * b.Chance, a.Damage.AddRange(b.Damage))))];
        List<ReactionBranch> WalkTree(ImmutableArray<EffectDef> nodes, RollResult? last = null)
        {
            var result = Empty();
            foreach (var node in nodes.IsDefault ? [] : nodes)
                result = Both(result, One(node, last));
            return result;
        }
        List<ReactionBranch> One(EffectDef effect, RollResult? last)
        {
            switch (effect)
            {
                case DamageEffect damage:
                    var type = damage.Type ?? (damage.Weapon ? st.User.Sheet.Weapon.DamageType : null);
                    return Rules.Environment.Reactions.Any(r => r.Damage is not null && r.Damage == type)
                        ? [new(1, [damage with { Type = type }])] : Empty();
                case AttackEffect attack:
                    if (ability.Target.Area.Shape != AreaShape.Single || target is null)
                        return WalkTree(attack.OnHit, last);
                    var odds = AttackOdds(attack, st, target);
                    var hit = new RollResult([], 0, odds.Modifier, 0, odds.Ac, true, false, false);
                    var normal = WalkTree(attack.OnHit, hit).Select(b => b with { Chance = b.Chance * (odds.Hit - odds.Crit) });
                    var critical = WalkTree((attack.OnHit.IsDefault ? [] : attack.OnHit).AddRange(attack.OnCrit.IsDefault ? [] : attack.OnCrit), hit with { Crit = true })
                        .Select(b => b with { Chance = b.Chance * odds.Crit });
                    return [.. normal, .. critical, new(1 - odds.Hit, [])];
                case SaveEffect save: return WalkTree(save.OnFail, last);
                case RepeatEffect repeat:
                    var repeated = Empty();
                    for (var n = 0; n < repeat.Times.At(st.User.Sheet.Level); n++) repeated = Both(repeated, WalkTree(repeat.Effects, last));
                    return repeated;
                case ChainEffect chain: return WalkTree(chain.Effects, last);
                case IfEffect conditional:
                    return WalkTree(Hooks.Holds(conditional.When, st.W,
                        new HookArgs(st.User.Id, target?.Id, last, Tile: st.Aim, Ability: ability.Id), Rules) ? conditional.Then : conditional.Else, last);
                case RandomEffect random:
                    var total = random.Table.IsDefault ? 0 : random.Table.Sum(e => Math.Max(0, e.Weight));
                    return total == 0 ? Empty() : [.. random.Table.Where(e => e.Weight > 0).SelectMany(e => WalkTree(e.Effects, last)
                        .Select(b => b with { Chance = b.Chance * e.Weight / total }))];
                default: return Empty();
            }
        }
        return WalkTree(Strikes.Sequence(ability, st.User.Sheet.Attacks));
    }
}
