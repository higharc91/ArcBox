using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Metamagic that shapes the spell after it (#110): Twinned Spell and Careful Spell are free uses of Sorcery that must come
/// before their spell, so the planner plans them as the spell's lead ("metamagic, then the spell"), valued by what they
/// change about it.
/// </summary>
internal sealed partial class Planner
{
    /// <summary>The metamagic kinds planned as a spell's lead.</summary>
    private static readonly ImmutableSortedSet<string> LeadingMetamagic = Ordinal.Set("careful", "twinned");

    private ImmutableArray<Usable>? _metamagic;

    /// <summary>The unit's Twinned and Careful Spell, in id order.</summary>
    private ImmutableArray<Usable> LeadingMetamagics() => _metamagic ??= [.. _usables.Where(u => u.Meta is { } k && LeadingMetamagic.Contains(k))];

    private readonly Dictionary<(Tile, AbilityId), (BattleState State, BattleWriter W)?> _shaped = [];

    /// <summary>
    /// The battle with the unit on <paramref name="t"/> (walked there) having used <paramref name="meta"/>: its condition on
    /// and its Sorcery spent. Null when it can't pay for it there.
    /// </summary>
    private (BattleState State, BattleWriter W)? ShapedOn(Tile t, Usable meta)
    {
        if (_shaped.TryGetValue((t, meta.Command), out var known))
            return known;
        var (state, _) = Hypo(t);
        (BattleState, BattleWriter)? shaped = null;
        if (Battle.CostRefusal(state.Units[_me.Id], meta.Ability) is null)
        {
            var after = Advance(state, new Use(_me.Id, meta.Command, new Target(), meta.Item));
            shaped = (after, new BattleWriter(after, _rules));
        }
        return _shaped[(t, meta.Command)] = shaped;
    }

    /// <summary>
    /// "Metamagic, then the spell" for <paramref name="usable"/>'s <paramref name="action"/> from <paramref name="t"/>: each
    /// Twinned or Careful Spell the unit can pay for there that changes it, with the action as it would then be and what it
    /// would be worth. Twinned, when a single-target spell finds a second target in range (the engine's own pick,
    /// <see cref="Battle.ResolveTarget"/>); Careful, when an area spell with a saving throw catches one of the unit's allies,
    /// who then pass the save. Only when the shaped spell beats the plain one by more than the metamagic costs.
    /// </summary>
    private IEnumerable<(Usable Meta, Action Action)> Shaped(Usable usable, Tile t, Action action)
    {
        var ability = usable.Ability;
        if (usable.Item is not null || usable.Meta is not null || !Metamagic.Shapes(ability) || LeadingMetamagics().IsEmpty)
            yield break;
        var def = ability.Target;
        var twinnable = def.Area.Shape == AreaShape.Single && def.Kind is TargetKind.Enemy or TargetKind.Ally or TargetKind.AnyUnit
                        && action.Affected.Length == 1;
        var spares = def.Area.Shape != AreaShape.Single && Scorer.All(ability.Effects).Any(e => e is SaveEffect)
                     && action.Affected.Any(id => id != _me.Id && !Scorer.Hostile(_me, _s.Units[id]));
        foreach (var meta in LeadingMetamagics())
        {
            if ((meta.Meta == "twinned" ? !twinnable : !spares) || ShapedOn(t, meta) is not { } shaped)
                continue;
            var me = shaped.State.Units[_me.Id];
            if (Battle.ResolveTarget(shaped.W, me, ability, action.Target, out var aim, out var affected) is not null
                || meta.Meta == "twinned" && affected.Length < 2)
                continue;
            var value = Value(usable, shaped.W, me, aim, affected);
            if (usable.Reacts)
            {
                var reaction = _scorer.ReactionWorth(new Strike(shaped.W, me, ability, aim, neutral: false), aim, affected, _p);
                if (shaped.State.Difficulty == Difficulty.Story && reaction.FriendlyDamage)
                    continue;
                value += reaction.Value;
            }
            if (Round(value - Hoard(meta)) > Round(action.Value))
                yield return (meta, new Action(action.Target, aim, affected, value));
        }
    }
}
