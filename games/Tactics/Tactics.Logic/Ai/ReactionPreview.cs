using System.Globalization;

namespace Tactics.Logic;

/// <summary>A reaction's immediate outcome, using public rolls and expected damage, never future RNG.</summary>
public sealed record ReactionPreview(string Outcome, double? IncomingDamage);

public static partial class Ai
{
    /// <summary>Read-only reaction guidance from the same effect maths as the AI policy. Does not apply commands or roll dice.</summary>
    public static ReactionPreview PreviewReaction(BattleState state, ReactionOffer offer, ReactionChoice option, RulesPack rules)
    {
        var beforeHit = offer.Cause is { Kind: ReactionTriggerKind.BeforeHit };
        if (!state.Units.TryGetValue(offer.Reactor, out var reactor) || offer.Cause is not { } trigger
            || !state.Units.TryGetValue(trigger.Actor, out var actor))
            return new("See the ability description for its effect.", null);
        var scorer = new Scorer(state, rules);
        double? incoming = beforeHit && trigger.Ability is { } source && rules.Abilities.ContainsKey(source)
            ? Math.Round(IncomingHit(scorer, reactor, actor, trigger), 1) : null;
        if (option.Id == Reactions.Pass)
            return new(trigger.Kind switch
            {
                ReactionTriggerKind.BeforeHit => "This hit lands. Keep your Reaction.",
                ReactionTriggerKind.LeavesReach => "The foe moves away. Keep your Reaction.",
                ReactionTriggerKind.SpellCast => "The spell continues. Keep your Reaction.",
                _ => "Keep your Reaction.",
            }, incoming);
        if (option.Ability is not { } id || !rules.Abilities.TryGetValue(id, out var ability))
            return new("See the ability description for its effect.", incoming);
        var effects = Scorer.All(ability.Effects).ToArray();
        if (effects.OfType<TransformEffect>().Any(e => e.Hook == "counterspell"))
        {
            var spell = state.Queue.OfType<RunEffectsStep>().Where(s => s.HookDepth == 0 && s.Source == actor.Id && s.Ability is not null)
                .Select(s => rules.Abilities.GetValueOrDefault(s.Ability!.Value)).FirstOrDefault(a => a?.Tier >= 1);
            if (spell is null) return new("No spell remains to counter.", incoming);
            var chance = spell.Tier <= Counterspell.SureTier ? 1
                : Math.Clamp((21 - (10 + spell.Tier - EffectContext.CastingModifierOf(reactor.Sheet))) / 20.0, 0, 1);
            return new(chance >= 1 ? $"Stops {spell.Name}." : $"{(100 * chance).ToString("0", CultureInfo.InvariantCulture)}% chance to stop {spell.Name}; otherwise it continues.", incoming);
        }
        if (beforeHit && ability.Target.Kind == TargetKind.Self)
        {
            var (miss, prevented) = Defends(scorer, ability, reactor, actor, trigger, accountForExistingAc: true);
            var outcome = miss ? "Turns this hit into a miss."
                : effects.Any(e => e is HalveHitEffect) ? "This hit still lands; halves its damage (rounded down)."
                : prevented > 0 ? $"This hit still lands; prevents about {Number(prevented)} damage."
                : trigger.Roll?.Crit == true ? "A critical hit still lands."
                : "This hit still lands.";
            return new(outcome, incoming);
        }
        var damage = scorer.Walk(ability.Effects, new Strike(scorer.W, reactor, ability, actor.Pos, neutral: false), actor).Damage.Mean;
        var text = $"Deals about {Number(damage)} damage on average";
        return new(text + (beforeHit ? "; the incoming hit still lands." : "; a hit isn't guaranteed."), incoming);
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
