using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// <c>counterspell</c> (args <c>{}</c>): a reaction to the <c>spellCast</c> trigger, aimed at the caster. The spell is the
/// first ability of tier 1 or more whose effects the caster still has queued. Up to tier <see cref="SureTier"/> it is
/// countered; above that the user rolls a check, d20 + its casting modifier (<c>mod.cast</c>) against 10 + tier
/// (<see cref="Rolled"/>), and counters it on a success. A countered spell's queued effects and attacks, and the reaction
/// windows still open for it, are dropped (<see cref="Countered"/>), and the caster's concentration on it ends.
/// </summary>
public sealed class Counterspell : INamedHook
{
    /// <summary>The highest tier countered without a check.</summary>
    public const int SureTier = 3;

    /// <inheritdoc />
    public string Name => "counterspell";

    /// <inheritdoc />
    public string? Describe(JsonElement args, RulesPack rules)
        => $"counters the spell being cast (spells above tier {SureTier} need a check)";

    /// <inheritdoc />
    public void Run(JsonElement args, EffectContext ctx)
    {
        if (ctx.Target is not { } caster)
            return;
        var w = ctx.W;
        AbilityDef? spell = null;
        foreach (var step in w.State.Queue.OfType<RunEffectsStep>())
            if (step is { HookDepth: 0, Ability: { } id } && step.Source == caster
                && ctx.Rules.Abilities.TryGetValue(id, out var ability) && ability.Tier >= 1)
            {
                spell = ability;
                break;
            }
        if (spell is null)
            return;

        if (spell.Tier > SureTier)
        {
            var check = Rolls.D20(w, ctx.CastingModifier, 10 + spell.Tier, Edge.None, critOn20: false, autoMissOn1: false);
            w.Emit(new Rolled(ctx.Source, RollKind.Check, check));
            if (!check.Success)
                return;
        }

        w.State = w.State with
        {
            Queue = w.State.Queue.RemoveAll(s => s switch
            {
                RunEffectsStep { HookDepth: 0 } run => run.Source == caster && run.Ability == spell.Id,
                AttackStep { HookDepth: 0 } attack => attack.Source == caster && attack.Ability == spell.Id,
                OfferReactionStep offer => offer.Trigger.Kind == ReactionTriggerKind.SpellCast && offer.Trigger.Actor == caster
                                           && offer.Trigger.Ability == spell.Id,
                _ => false,
            }),
        };
        w.Emit(new Countered(caster, spell.Id, ctx.Source));
        if (w.Unit(caster).Concentrating == spell.Id)
            Conditions.BreakConcentration(w, caster);
    }

    /// <inheritdoc />
    public IEnumerable<string> Monsters(JsonElement args) => [];

    /// <inheritdoc />
    public void Validate(JsonElement args, Action<string> error) => NamedHooks.RequireObject(Name, args, error);
}
