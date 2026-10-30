namespace Tactics.Logic;

/// <summary>
/// The situation a hook fires in: whose passives fire (<see cref="Owner"/>), the other unit involved (the attack's target,
/// whoever dealt the damage, …), the roll, an amount (damage taken), a tile (the one entered, or the target's), the
/// ability, the attack kind (<c>"weapon"</c> or <c>"spell"</c>), the roll's edge and the damage type.
/// </summary>
public sealed record HookArgs(UnitId Owner, UnitId? Other = null, RollResult? Roll = null, int Amount = 0, Tile? Tile = null,
    AbilityId? Ability = null, string? AttackKind = null, Edge Edge = Edge.None, string? DamageType = null);

/// <summary>
/// Fires passives on hooks (spec §4.4). A passive fires when its <see cref="PassiveDef.Hook"/> matches, its limits allow
/// it and its <see cref="PassiveDef.When"/> holds; its effects then run from the owner on <see cref="HookArgs.Other"/> (or
/// the owner), right away: they go to the front of the queue, ahead of whatever was already waiting.
/// </summary>
public static class Hooks
{
    /// <summary>
    /// How deeply hooks may nest (a hook's effects firing another hook, and so on). Going deeper throws
    /// <see cref="RulesInvariantException"/>.
    /// </summary>
    public const int MaxDepth = 8;

    /// <summary>
    /// Fires <paramref name="hook"/> for <see cref="HookArgs.Owner"/> (Up or Downed): its sheet passives in sheet order,
    /// then its conditions' passives in the order the conditions were applied. Each passive that fires is spent (once per
    /// turn, once per round, per-battle uses) and queues a <see cref="RunEffectsStep"/> that emits
    /// <see cref="PassiveTriggered"/> and runs its effects, following <see cref="HookArgs.Roll"/>.
    /// </summary>
    public static void Fire(BattleWriter w, HookKind hook, HookArgs args, RulesPack rules)
    {
        var steps = new List<Step>();
        Collect(w, hook, args, rules, steps);
        if (steps.Count > 0)
            w.PushFront([.. steps]);
    }

    /// <summary>Fires <paramref name="hook"/> for every unit, in sorted <see cref="UnitId"/> order (their effects run in that order).</summary>
    public static void FireAll(BattleWriter w, HookKind hook, RulesPack rules)
    {
        var steps = new List<Step>();
        CollectAll(w, hook, rules, steps);
        if (steps.Count > 0)
            w.PushFront([.. steps]);
    }

    /// <summary>Like <see cref="FireAll"/>, but adds the steps to <paramref name="into"/> instead of queueing them.</summary>
    internal static void CollectAll(BattleWriter w, HookKind hook, RulesPack rules, List<Step> into)
    {
        foreach (var id in w.State.Units.Keys)
            Collect(w, hook, new HookArgs(id), rules, into);
    }

    /// <summary>Like <see cref="Fire"/>, but adds the steps to <paramref name="into"/> instead of queueing them.</summary>
    internal static void Collect(BattleWriter w, HookKind hook, HookArgs args, RulesPack rules, List<Step> into)
    {
        if (!w.State.Units.TryGetValue(args.Owner, out var owner) || owner.Life is LifeState.Dead or LifeState.Out)
            return;

        var passives = Modifiers.Passives(owner, rules)
            .Concat(Conditions.Defs(owner, rules).SelectMany(c => c.Passives.IsDefault ? [] : c.Passives))
            .Where(p => p.Hook == hook && !p.Effects.IsDefaultOrEmpty)
            .ToArray();
        foreach (var passive in passives)
        {
            owner = w.Unit(args.Owner);
            if (!Available(owner, passive) || passive.When is { } when && !Holds(when, w, args, rules))
                continue;

            var depth = w.HookDepth + 1;
            if (depth > MaxDepth)
                throw new RulesInvariantException(
                    $"Hooks nested deeper than {MaxDepth}: '{passive.Id}' of {owner.Id} on {hook}.");

            w.Set(Spend(owner, passive));
            var target = args.Other ?? owner.Id;
            into.Add(new RunEffectsStep(owner.Id, target, args.Tile ?? w.Unit(target).Pos, null, passive.Effects, args.Roll,
                HookDepth: depth, Passive: passive.Id));
        }
    }

    private static bool Available(UnitState owner, PassiveDef passive)
        => !(passive.OncePerTurn && owner.UsedThisTurn.Contains(passive.Id))
           && !(passive.OncePerRound && owner.UsedThisRound.Contains(passive.Id))
           && !(passive.UsesPerBattle is { } uses && owner.UsesLeft.GetValueOrDefault(passive.Id, uses) < 1);

    private static UnitState Spend(UnitState owner, PassiveDef passive) => owner with
    {
        UsedThisTurn = passive.OncePerTurn ? owner.UsedThisTurn.Add(passive.Id) : owner.UsedThisTurn,
        UsedThisRound = passive.OncePerRound ? owner.UsedThisRound.Add(passive.Id) : owner.UsedThisRound,
        UsesLeft = passive.UsesPerBattle is { } uses
            ? owner.UsesLeft.SetItem(passive.Id, owner.UsesLeft.GetValueOrDefault(passive.Id, uses) - 1)
            : owner.UsesLeft,
    };

    /// <summary>
    /// Whether <paramref name="when"/> holds for <paramref name="args"/> (see <see cref="WhenDef"/>): every key present must
    /// hold; <c>all</c> needs every entry, <c>any</c> at least one, <c>not</c> its entry to fail.
    /// </summary>
    public static bool Holds(WhenDef when, BattleWriter w, HookArgs args, RulesPack rules)
    {
        var state = w.State;
        var owner = state.Units[args.Owner];
        var subject = state.Units[args.Other ?? args.Owner];

        if (!when.All.IsDefault && !when.All.All(x => Holds(x, w, args, rules)))
            return false;
        if (!when.Any.IsDefault && !when.Any.Any(x => Holds(x, w, args, rules)))
            return false;
        if (when.Not is { } not && Holds(not, w, args, rules))
            return false;
        if (!when.Weapon.IsDefault && !(args.AttackKind == "weapon" && when.Weapon.Any(p => WeaponIs(owner.Sheet.Weapon, p))))
            return false;
        if (when.Advantage is { } advantage && (args.Edge == Edge.Advantage) != advantage)
            return false;
        if (when.Flanked is { } flanked && (args.Other is { } other && Modifiers.Flanked(state, owner.Id, other, rules)) != flanked)
            return false;
        if (when.TargetHasCondition is { } condition && !Conditions.Has(subject, condition, rules))
            return false;
        if (when.HpBelow is { } percent && subject.Hp * 100 >= percent * subject.Sheet.MaxHp)
            return false;
        if (when.HeightAbove is { } levels && w.Terrain.HeightAt(owner.Pos) - w.Terrain.HeightAt(subject.Pos) < levels)
            return false;
        if (when.IsSpell is { } spell && IsSpell(args, rules) != spell)
            return false;
        if (when.DamageType is { } type && args.DamageType != type)
            return false;
        return when.TargetTag is not { } tag || subject.Sheet.Tags.Contains(tag);
    }

    private static bool WeaponIs(WeaponDef weapon, string property) => property switch
    {
        "finesse" => weapon.Finesse,
        "ranged" => weapon.Ranged,
        "melee" => !weapon.Ranged,
        "reach" => weapon.Reach,
        _ => weapon.Id == property || weapon.Tags.Contains(property),
    };

    private static bool IsSpell(HookArgs args, RulesPack rules)
        => args.AttackKind == "spell"
           || args.Ability is { } id && rules.Abilities.TryGetValue(id, out var ability) && ability.Tier >= 1;
}
