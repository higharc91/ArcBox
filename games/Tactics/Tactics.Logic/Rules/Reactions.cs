using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>What a reaction answers. JSON and <see cref="ReactionOffer.Trigger"/>: <c>"leavesReach"</c>, <c>"beforeHit"</c>, <c>"spellCast"</c>.</summary>
public enum ReactionTriggerKind
{
    /// <summary>A unit is about to step out of a hostile's melee reach.</summary>
    [JsonStringEnumMemberName("leavesReach")] LeavesReach,
    /// <summary>An attack roll has hit <see cref="ReactionTrigger.Target"/> and its effects are about to land.</summary>
    [JsonStringEnumMemberName("beforeHit")] BeforeHit,
    /// <summary>A unit is casting a spell.</summary>
    [JsonStringEnumMemberName("spellCast")] SpellCast,
}

/// <summary>
/// Something units may react to: who did it (<see cref="Actor"/>), to whom, with which ability and roll, and for
/// <see cref="ReactionTriggerKind.LeavesReach"/> the tile being stepped onto (<see cref="To"/>).
/// </summary>
public sealed record ReactionTrigger(ReactionTriggerKind Kind, UnitId Actor, UnitId? Target, AbilityId? Ability,
    RollResult? Roll, Tile? To = null);

/// <summary>
/// Who may react to what. An offer's options are always <c>"pass"</c> first, then the opportunity attack, then reaction
/// abilities in sheet order.
/// </summary>
public static class Reactions
{
    /// <summary>The choice id that declines a reaction.</summary>
    public const string Pass = "pass";

    /// <summary>The choice id of an opportunity attack: one attack with the reactor's weapon, for its Reaction.</summary>
    public const string Opportunity = "opportunity";

    /// <summary>The prefix of the <see cref="AbilityDef.Ai"/> tag that names the trigger a reaction ability answers.</summary>
    public const string ReactsToTag = "reactsTo:";

    /// <summary>
    /// Reactions any unit may take for this trigger, in sorted UnitId order (opportunity attack, or abilities with
    /// cost.reaction and a matching "reactsTo" in AbilityDef.Ai tags: "beforeHit", "leavesReach", "spellCast").
    /// <para>A reactor is Up, not the actor, and has its Reaction. An ability is offered when the reactor knows it, can pay
    /// for it and carries the tag <c>"reactsTo:&lt;trigger&gt;"</c>; it aims at the reactor for a <c>self</c> ability and at
    /// the actor otherwise. Per trigger:</para>
    /// <list type="bullet">
    /// <item><c>leavesReach</c>: the actor is Up with HP left and hostile, and the reactor's melee weapon reaches the actor's tile but not
    /// <see cref="ReactionTrigger.To"/>. Offers the opportunity attack and matching abilities.</item>
    /// <item><c>beforeHit</c>: the reactor is <see cref="ReactionTrigger.Target"/>.</item>
    /// <item><c>spellCast</c>: the actor is hostile and within the ability's range.</item>
    /// </list>
    /// Units with only <c>"pass"</c> get no offer.
    /// </summary>
    public static ImmutableArray<ReactionOffer> OffersFor(BattleWriter w, ReactionTrigger trigger, RulesPack rules)
    {
        var offers = ImmutableArray.CreateBuilder<ReactionOffer>();
        foreach (var reactor in w.State.Units.Values)
            if (OfferFor(w, reactor, trigger, rules) is { } offer)
                offers.Add(offer);
        return offers.ToImmutable();
    }

    /// <summary>The trigger's name in <see cref="ReactionOffer.Trigger"/> and in content tags.</summary>
    public static string NameOf(ReactionTriggerKind kind) => kind switch
    {
        ReactionTriggerKind.LeavesReach => "leavesReach",
        ReactionTriggerKind.BeforeHit => "beforeHit",
        ReactionTriggerKind.SpellCast => "spellCast",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ReactionOffer? OfferFor(BattleWriter w, UnitState reactor, ReactionTrigger trigger, RulesPack rules)
    {
        if (reactor.Id == trigger.Actor || reactor.Life != LifeState.Up || !reactor.Economy.Reaction)
            return null;

        var actor = w.Unit(trigger.Actor);
        var options = ImmutableArray.CreateBuilder<ReactionChoice>();
        options.Add(new ReactionChoice(Pass, null, null));

        bool eligible;
        switch (trigger.Kind)
        {
            case ReactionTriggerKind.LeavesReach:
                eligible = actor.Life == LifeState.Up && actor.Hp > 0 && Movement.Hostile(reactor, actor) && LeavesReach(w.Terrain, reactor, actor.Pos, trigger.To);
                if (eligible)
                    options.Add(new ReactionChoice(Opportunity, RulesPack.CommonAttack.Id, actor.Id));
                break;
            case ReactionTriggerKind.BeforeHit:
                eligible = trigger.Target == reactor.Id;
                break;
            case ReactionTriggerKind.SpellCast:
                eligible = Movement.Hostile(reactor, actor);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Kind, null);
        }
        if (!eligible)
            return null;

        var tag = ReactsToTag + NameOf(trigger.Kind);
        foreach (var id in reactor.Sheet.Abilities)
        {
            if (!rules.Abilities.TryGetValue(id, out var ability) || ability.Cost.Reaction <= 0
                || ability.Ai is not { } ai || ai.Tags.IsDefault || !ai.Tags.Contains(tag)
                || Battle.CostRefusal(reactor, ability) is not null)
                continue;
            if (trigger.Kind == ReactionTriggerKind.SpellCast && Reach.Distance(reactor.Pos, actor.Pos) > ability.Target.Range)
                continue;
            options.Add(new ReactionChoice(id.Value, id, ability.Target.Kind == TargetKind.Self ? reactor.Id : actor.Id));
        }

        return options.Count > 1 ? new ReactionOffer(reactor.Id, options.ToImmutable(), NameOf(trigger.Kind)) : null;
    }

    /// <summary>True when the reactor's melee weapon reaches <paramref name="from"/> but not <paramref name="to"/>.</summary>
    private static bool LeavesReach(Terrain terrain, UnitState reactor, Tile from, Tile? to)
    {
        var weapon = reactor.Sheet.Weapon;
        if (weapon.Ranged || !Reach.Melee(terrain, reactor.Pos, from, weapon.Reach))
            return false;
        return to is not { } next || !terrain.Map.InBounds(next) || !Reach.Melee(terrain, reactor.Pos, next, weapon.Reach);
    }
}

/// <summary>
/// Opens one reactor's reaction window for <see cref="Trigger"/>, re-checking that it may still react (an earlier
/// reaction may have changed things). A player-controlled reactor gets the <see cref="ReactionOffer"/> as
/// <see cref="BattleState.Pending"/> (<see cref="ReactionOffered"/>) and the queue pauses until its <see cref="React"/>.
/// An AI-controlled reactor is never offered: it takes an opportunity attack whenever it has one and otherwise passes
/// silently (Task 15 replaces this with the AI's reaction policy).
/// </summary>
public sealed record OfferReactionStep(UnitId Reactor, ReactionTrigger Trigger) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var offer = Reactions.OffersFor(w, Trigger, rules).FirstOrDefault(o => o.Reactor == Reactor);
        if (offer is null)
            return;

        if (w.Unit(Reactor).Controller is AiControlled)
        {
            if (offer.Options.FirstOrDefault(o => o.Id == Reactions.Opportunity) is not { } attack)
                return;
            w.Emit(new Reacted(Reactor, attack.Id));
            w.PushFront(new ReactionStep(Reactor, attack));
            return;
        }

        w.State = w.State with { Pending = offer };
        w.Emit(new ReactionOffered(offer));
    }
}

/// <summary>
/// Resolves a taken reaction. An opportunity attack spends the Reaction, emits <see cref="AbilityUsed"/> for
/// <c>common.attack</c> and runs its effects once on the target (one attack, whatever the sheet's attack count). A reaction
/// ability is paid and run like a used ability (<see cref="PayCostStep"/>, then <see cref="RunEffectsStep"/>).
/// </summary>
public sealed record ReactionStep(UnitId Reactor, ReactionChoice Choice) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var reactor = w.Unit(Reactor);
        var target = Choice.Target is { } id ? w.Unit(id) : reactor;
        var ability = Choice.Id == Reactions.Opportunity
            ? rules.Abilities[RulesPack.CommonAttack.Id]
            : rules.Abilities[Choice.Ability ?? throw new InvalidOperationException($"Reaction '{Choice.Id}' names no ability.")];
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        var run = new RunEffectsStep(Reactor, target.Id, target.Pos, ability.Id, effects);

        if (Choice.Id != Reactions.Opportunity)
        {
            w.PushFront(new PayCostStep(Reactor, ability.Id, new Target(Unit: target.Id)), run);
            return;
        }

        w.Set(reactor with { Economy = reactor.Economy with { Reaction = false } });
        w.Emit(new AbilityUsed(Reactor, ability.Id, new Target(Unit: target.Id), ability.Presentation));
        w.PushFront(run);
    }
}
