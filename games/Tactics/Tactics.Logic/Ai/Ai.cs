namespace Tactics.Logic;

/// <summary>
/// The utility AI (spec §5): one planner for enemies, absent players' characters and timed-out turns, each with its own
/// profile, and the reaction policy of AI-controlled units. It never draws from the battle's RNG, so the same state always
/// gives the same plan and the same reactions.
/// </summary>
public static class Ai
{
    /// <summary>
    /// The whole turn for <paramref name="unit"/>, whose turn it must be (otherwise the plan has no commands): moves and
    /// abilities in order, ending with <see cref="EndTurn"/>, each of which <see cref="Battle.Apply"/> accepts in the state
    /// the plan expects (a move cut short by an opportunity attack leaves the rest stale: plan again). The profile is the
    /// unit's own (<see cref="AiProfile.For"/>) unless <see cref="AiOptions.ProfileOverride"/> names another. See
    /// <see cref="Planner"/> for how the turn is chosen, and <see cref="Companion"/> for the companion profile.
    /// </summary>
    public static TurnPlan Plan(BattleState s, UnitId unit, RulesPack rules, AiOptions? options = null)
        => Planner.Plan(s, unit, rules, options);

    /// <summary>
    /// The answer to <paramref name="offer"/> (one of its options) for an AI-controlled reactor, or for a player who didn't
    /// answer in time (spec §5.5). Of the options, in this order:
    /// <list type="number">
    /// <item>A defensive reaction (an ability aimed at the reactor itself) to a <see cref="ReactionTriggerKind.BeforeHit"/>
    /// on the reactor, when it turns the pending hit into a miss against the known roll (the AC it adds, from
    /// <c>statMod</c>s and applied conditions, lifts the AC above the roll's total; never against a natural 20), or when it
    /// prevents at least 25% of the reactor's max HP of the hit's expected damage (its expected temporary HP, beyond what
    /// the reactor has, up to that damage). The one that turns the hit into a miss first, then the one preventing most.</item>
    /// <item>The opportunity attack, or another reaction aimed at the actor (a rebuke, a counterspell), when its expected
    /// value is above 0: what it is worth to the reactor's profile (<see cref="AiProfile.For"/>) less what its resources
    /// cost (<see cref="AiProfile.Spend"/>). A counterspell is worth what the spell being cast is worth to its caster, times
    /// the chance of countering it (a check above <see cref="Counterspell.SureTier"/>). The best one.</item>
    /// <item>Otherwise <see cref="Reactions.Pass"/>.</item>
    /// </list>
    /// Ties go to the earlier option. Rolls nothing.
    /// </summary>
    public static ReactionChoice React(BattleState s, ReactionOffer offer, ReactionTrigger trigger, RulesPack rules)
    {
        var pass = offer.Options.FirstOrDefault(o => o.Id == Reactions.Pass) ?? new ReactionChoice(Reactions.Pass, null, null);
        if (!s.Units.TryGetValue(offer.Reactor, out var reactor) || !s.Units.TryGetValue(trigger.Actor, out var actor))
            return pass;
        var scorer = new Scorer(s, rules);
        var profile = AiProfile.For(s, reactor.Id, rules);

        (ReactionChoice Choice, bool Miss, double Prevented)? defence = null;
        (ReactionChoice Choice, double Ev)? strike = null;
        foreach (var option in offer.Options)
        {
            if (option.Id == Reactions.Pass)
                continue;
            var ability = option.Id == Reactions.Opportunity ? rules.Abilities.GetValueOrDefault(RulesPack.CommonAttack.Id)
                : option.Ability is { } id ? rules.Abilities.GetValueOrDefault(id) : null;
            if (ability is null)
                continue;
            if (option.Id != Reactions.Opportunity && ability.Target.Kind == TargetKind.Self)
            {
                if (trigger.Kind != ReactionTriggerKind.BeforeHit || trigger.Target != reactor.Id)
                    continue;
                var (miss, prevented) = Defends(scorer, ability, reactor, actor, trigger);
                if ((miss || prevented >= 0.25 * reactor.Sheet.MaxHp)
                    && (defence is not { } d || miss && !d.Miss || miss == d.Miss && prevented > d.Prevented))
                    defence = (option, miss, prevented);
            }
            else
            {
                var target = option.Target is { } t && s.Units.TryGetValue(t, out var u) ? u : actor;
                var ev = Strikes(scorer, profile, ability, reactor, target, trigger, option.Id == Reactions.Opportunity);
                if (ev > 0 && (strike is not { } best || ev > best.Ev))
                    strike = (option, ev);
            }
        }
        return defence?.Choice ?? strike?.Choice ?? pass;
    }

    /// <summary>
    /// Whether <paramref name="ability"/> turns the hit <paramref name="trigger"/> describes into a miss, and how much of
    /// its expected damage it prevents otherwise (its temporary HP).
    /// </summary>
    private static (bool Miss, double Prevented) Defends(Scorer scorer, AbilityDef ability, UnitState reactor, UnitState actor,
        ReactionTrigger trigger)
    {
        var rules = scorer.Rules;
        var ac = 0;
        foreach (var effect in Scorer.All(ability.Effects))
            ac += effect switch
            {
                StatModEffect m => m.Mods.Ac,
                ApplyEffect a => ConditionAc(a.Condition, rules),
                _ => 0,
            };
        var roll = trigger.Roll;
        var miss = ac > 0 && roll is { Crit: false } && roll.Total < roll.Vs + ac;

        var hit = 0.0;
        if (trigger.Ability is { } id && rules.Abilities.TryGetValue(id, out var attack) && Scorer.FirstAttack(attack.Effects) is { } def)
        {
            var crit = roll?.Crit == true;
            var onHit = def.OnHit.IsDefault ? [] : def.OnHit;
            var branch = crit && !def.OnCrit.IsDefaultOrEmpty ? onHit.AddRange(def.OnCrit) : onHit;
            hit = scorer.Walk(branch, new Strike(scorer.W, actor, attack, reactor.Pos, neutral: false), reactor, crit: crit, last: roll)
                .Damage.Mean;
        }
        if (miss)
            return (true, hit);
        var temp = scorer.Walk(ability.Effects, new Strike(scorer.W, reactor, ability, reactor.Pos, neutral: false), reactor).TempHp;
        return (false, Math.Min(hit, Math.Max(0, temp - reactor.TempHp)));
    }

    /// <summary>The AC a condition (and the ones it implies) adds.</summary>
    private static int ConditionAc(ConditionId id, RulesPack rules)
    {
        var ac = 0;
        var seen = new List<ConditionId>();
        for (ConditionId? next = id; next is { } c && !seen.Contains(c) && rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
        {
            seen.Add(c);
            ac += def.Static?.Ac ?? 0;
        }
        return ac;
    }

    /// <summary>
    /// What striking <paramref name="target"/> with <paramref name="ability"/> as a reaction is worth to
    /// <paramref name="profile"/>, less its cost; a counterspell is worth the spell it stops.
    /// </summary>
    private static double Strikes(Scorer scorer, AiProfile profile, AbilityDef ability, UnitState reactor, UnitState target,
        ReactionTrigger trigger, bool opportunity)
    {
        var cost = opportunity ? 0 : profile.Spend(reactor, ability.Cost, item: false);
        if (Scorer.All(ability.Effects).Any(e => e is TransformEffect { Hook: "counterspell" }))
            return trigger.Kind == ReactionTriggerKind.SpellCast ? Countering(scorer, profile, reactor, target) - cost : 0;
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        var outcome = scorer.Walk(effects, new Strike(scorer.W, reactor, ability, target.Pos, neutral: false), target);
        return scorer.Worth(reactor, target, outcome, profile) - cost;
    }

    /// <summary>
    /// What countering <paramref name="caster"/>'s spell is worth: the spell's queued effects on each unit, as their worth
    /// to the caster, times the chance <paramref name="reactor"/> counters it.
    /// </summary>
    private static double Countering(Scorer scorer, AiProfile profile, UnitState reactor, UnitState caster)
    {
        var rules = scorer.Rules;
        var units = scorer.State.Units;
        AbilityDef? spell = null;
        var worth = 0.0;
        foreach (var step in scorer.State.Queue.OfType<RunEffectsStep>())
        {
            if (step is not { HookDepth: 0, Ability: { } id } || step.Source != caster.Id
                || !rules.Abilities.TryGetValue(id, out var ability) || ability.Tier < 1 || spell is not null && ability != spell)
                continue;
            spell = ability;
            if (step.Target is { } t && units.TryGetValue(t, out var target))
                worth += scorer.Worth(caster, target,
                    scorer.Walk(step.Effects, new Strike(scorer.W, caster, ability, step.Aim, neutral: false), target), profile);
        }
        if (spell is null)
            return 0;
        var chance = spell.Tier <= Counterspell.SureTier
            ? 1
            : Math.Clamp((21 - (10 + spell.Tier - EffectContext.CastingModifierOf(reactor.Sheet))) / 20.0, 0, 1);
        return chance * worth;
    }
}
