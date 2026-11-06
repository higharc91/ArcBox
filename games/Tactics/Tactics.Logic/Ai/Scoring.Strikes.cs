using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>
    /// What the first 1, 2, … <paramref name="count"/> attacks of the weapon attack <paramref name="ability"/> are expected to
    /// do to <paramref name="target"/> (element k is the first k + 1 attacks), as the rules make them (<see cref="StrikeStep"/>):
    /// <list type="bullet">
    /// <item>Without <see cref="AbilityDef.RiderOnce"/>, each attack runs the ability's unit blocks; the user's once-per-turn
    /// passives (sneak attack) count on the first only, as they do when the list is walked whole.</item>
    /// <item>With it, the attacks run the whole list until one hits and <see cref="Strikes.Plain"/> after: with p the chance
    /// the first attack roll hits, k attacks are R(k) = p · (the hit + k − 1 plain attacks) + (1 − p) · (the miss + R(k − 1)),
    /// so the rider counts once, with the chance that any of them hits.</item>
    /// </list>
    /// A target with a coating that the first damage uses up (<see cref="WalkDamageTaken"/>) is walked one prefix at a time,
    /// the attacks one after another (riders on the first), so the coating is spent once along every branch.
    /// </summary>
    public ImmutableArray<Outcome> StrikeOutcomes(AbilityDef ability, int count, Strike st, UnitState target)
    {
        count = Math.Max(1, count);
        var full = UnitBlocks(ability.Effects);
        var outcomes = ImmutableArray.CreateBuilder<Outcome>(count);
        if (!st.ProjectingDamageTaken && HasConsumableDamageTaken(target))
        {
            var rest = ability.RiderOnce ? Strikes.Plain(ability) : full;
            var fired = st.Fired.ToArray();
            for (var k = 1; k <= count; k++)
            {
                st.Fired.Clear();
                st.Fired.UnionWith(fired);
                outcomes.Add(Walk([.. full, .. Enumerable.Repeat(rest, k - 1).SelectMany(e => e)], st, target));
            }
            return outcomes.MoveToImmutable();
        }
        if (!ability.RiderOnce || count == 1)
        {
            outcomes.Add(Walk(full, st, target));
            if (count > 1)
            {
                var again = Walk(full, st, target);   // the once-per-turn passives have had their chance
                for (var k = 2; k <= count; k++)
                    outcomes.Add(outcomes[^1].Then(again));
            }
            return outcomes.MoveToImmutable();
        }

        var p = FirstAttack(full) is { } attack ? AttackOdds(attack, st, target).Hit : 1;
        st.Force = true;
        var landed = Walk(full, st, target);
        st.Force = null;
        var plain = Walk(Strikes.Plain(ability), st, target);
        st.Force = false;
        var missed = Walk(full, st, target);
        st.Force = null;
        var plains = Outcome.None;
        var r = Outcome.None;
        for (var k = 1; k <= count; k++)
        {
            r = Outcome.Mix([(p, landed.Then(plains)), (1 - p, missed.Then(r))]);
            outcomes.Add(r);
            plains = plains.Then(plain);
        }
        return outcomes.MoveToImmutable();
    }

    /// <summary>
    /// What 1, 2, … <paramref name="count"/> more attacks of <paramref name="ability"/> are expected to do to
    /// <paramref name="target"/> after the attacks before them went to another unit (element k is k + 1 attacks): plain
    /// attacks for a <see cref="AbilityDef.RiderOnce"/> ability (its rider is expected to have landed already), the whole
    /// list otherwise.
    /// </summary>
    public ImmutableArray<Outcome> FollowUps(AbilityDef ability, int count, Strike st, UnitState target)
    {
        var each = ability.RiderOnce ? Strikes.Plain(ability) : UnitBlocks(ability.Effects);
        var outcomes = ImmutableArray.CreateBuilder<Outcome>(Math.Max(0, count));
        if (!st.ProjectingDamageTaken && HasConsumableDamageTaken(target))
        {
            var fired = st.Fired.ToArray();
            for (var k = 1; k <= count; k++)
            {
                st.Fired.Clear();
                st.Fired.UnionWith(fired);
                outcomes.Add(Walk([.. Enumerable.Repeat(each, k).SelectMany(e => e)], st, target));
            }
            return outcomes.MoveToImmutable();
        }
        var one = Walk(each, st, target);
        for (var k = 1; k <= count; k++)
            outcomes.Add(k == 1 ? one : outcomes[^1].Then(one));
        return outcomes.MoveToImmutable();
    }

    /// <summary>The blocks of <paramref name="effects"/> that run on a unit (not the tile ones).</summary>
    public static ImmutableArray<EffectDef> UnitBlocks(ImmutableArray<EffectDef> effects)
        => effects.IsDefault ? [] : [.. effects.Where(e => !e.OnTile)];
}
