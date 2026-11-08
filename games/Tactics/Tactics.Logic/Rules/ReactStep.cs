using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>Last step of an opted-in use. Its hit marker survives a pending reaction and save/load.</summary>
public sealed record ReactStep(UnitId Source, AbilityId Ability, Tile Aim, ImmutableArray<Tile> Footprint, int Resolution,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<UnitId> DirectTargets = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Hit = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Completed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<ReactionDamage> Observed = default) : Step
{
    public override void Run(BattleWriter w, RulesPack rules)
    {
        if (Completed)
            return;
        if (!w.State.Environment || !rules.Abilities.TryGetValue(Ability, out var ability))
            return;
        // An interrupted caster may never have produced the element. Damage already landed still reacts.
        if (w.Unit(Source).Life != LifeState.Up && !Hit && Observed.IsDefaultOrEmpty)
            return;
        if (ability.Target.Area.Shape == AreaShape.Single && EffectTree.All(ability.Effects).Any(e => e is AttackEffect) && !Hit)
            return;
        w.PushFront(this with { Completed = true });
        // Static dice cover empty footprints and successful saves. Selected runtime branches replace that
        // type's static dice, without multiplying the spell by the number of direct targets it hit.
        var observed = Observed.IsDefault ? [] : Observed;
        var selectedTypes = observed.Select(d => d.Effect.Type).ToHashSet();
        var damage = EffectTree.Damage(ability.Effects, w.Unit(Source).Sheet)
            .Where(d => !selectedTypes.Contains(d.Type)).Concat(observed.Select(d => d.Effect)).ToImmutableArray();
        var types = damage.Select(d => d.Type).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var changed = new HashSet<Tile>();
        foreach (var reaction in rules.Environment.Reactions)
        {
            if (reaction.Damage is null || !types.Contains(reaction.Damage))
                continue;
            var available = Footprint.Where(t => !changed.Contains(t)).ToImmutableArray();
            if (reaction.Conduct is { } conduct)
            {
                var tiles = Surfaces.Conducted(w, available, conduct.Reach);
                if (tiles.IsEmpty)
                    continue;
                var effects = damage.Where(d => d.Type == reaction.Damage).Cast<EffectDef>().ToImmutableArray();
                var direct = DirectTargets.IsDefault ? [] : DirectTargets;
                var victims = w.State.Units.Values.Where(u => u.Life == LifeState.Up && tiles.Contains(u.Pos) && !direct.Contains(u.Id)).ToArray();
                w.Emit(new TileReacted(reaction.Id, Source, tiles));
                var save = new SaveEffect(conduct.Save, null, effects, [], conduct.Half);
                // Environmental damage is not sculpted or extended as another direct spell hit.
                w.PushFront([.. victims.Select(u => new RunEffectsStep(Source, u.Id, u.Pos, null, [save], HookDepth: 1))]);
                changed.UnionWith(tiles);
                continue;
            }
            var matching = available.Where(t => Surfaces.Matches(w, rules.Environment, reaction.Tiles, t)).Order().ToImmutableArray();
            if (matching.IsEmpty)
                continue;
            w.Emit(new TileReacted(reaction.Id, Source, matching));
            if (reaction.Ends)
                Surfaces.RemoveTiles(w, z => z.Surface && reaction.Tiles == "surface:" + z.Kind, matching);
            else if (reaction.Surface is { } kind)
            {
                if (reaction.Consumes)
                    Surfaces.Consume(w, reaction.Tiles, matching);
                Surfaces.Create(w, kind, matching, Source, reaction.Id, reaction.Rounds,
                    reaction.Passives.IsDefaultOrEmpty ? null : reaction.Passives);
            }
            changed.UnionWith(matching);
        }
    }
}

/// <summary>The first direct target's resolved dice for a damage type, before saves, resistance and critical doubling.</summary>
public sealed record ReactionDamage(UnitId Target, DamageEffect Effect);

/// <summary>Shared effect-tree queries for rules, previews and content validation.</summary>
/// <summary>Read-only traversal of nested effect definitions.</summary>
public static class EffectTree
{
    public static IEnumerable<EffectDef> All(ImmutableArray<EffectDef> effects)
    {
        foreach (var effect in effects.IsDefault ? [] : effects)
        {
            yield return effect;
            var children = effect switch
            {
                AttackEffect a => All(a.OnHit).Concat(All(a.OnMiss)).Concat(All(a.OnCrit)),
                SaveEffect s => All(s.OnFail).Concat(All(s.OnSuccess)),
                RepeatEffect r => All(r.Effects), ChainEffect c => All(c.Effects),
                IfEffect i => All(i.Then).Concat(All(i.Else)), RandomEffect r => r.Table.SelectMany(t => All(t.Effects)),
                _ => [],
            };
            foreach (var child in children)
                yield return child;
        }
    }

    public static ImmutableArray<DamageEffect> Damage(ImmutableArray<EffectDef> effects, UnitSheet source)
    {
        var result = ImmutableArray.CreateBuilder<DamageEffect>();
        void Walk(ImmutableArray<EffectDef> nodes, int repeats = 1)
        {
            foreach (var effect in nodes.IsDefault ? [] : nodes)
                switch (effect)
                {
                    case DamageEffect d:
                        for (var n = 0; n < repeats; n++)
                            result.Add(d with { Type = d.Type ?? (d.Weapon ? source.Weapon.DamageType : null) });
                        break;
                    case AttackEffect a: Walk(a.OnHit, repeats); break;
                    case SaveEffect s: Walk(s.OnFail, repeats); break;
                    case RepeatEffect r: Walk(r.Effects, repeats * r.Times.At(source.Level)); break;
                    case ChainEffect c: Walk(c.Effects, repeats); break;
                    // These choose a branch at runtime. DamageBlock records only the branch actually run.
                    case IfEffect or RandomEffect: break;
                }
        }
        Walk(effects);
        return result.ToImmutable();
    }
}
