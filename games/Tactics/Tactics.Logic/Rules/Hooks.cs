using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// The situation a hook fires in: whose passives fire (<see cref="Owner"/>), the other unit involved (the attack's target,
/// whoever dealt the damage, …), the roll, an amount (damage taken), a tile (the one entered, or the target's), the
/// ability, the attack kind (<c>"weapon"</c> or <c>"spell"</c>), the roll's edge and the damage type.
/// </summary>
public sealed record HookArgs(UnitId Owner, UnitId? Other = null, RollResult? Roll = null, int Amount = 0, Tile? Tile = null,
    AbilityId? Ability = null, string? AttackKind = null, Edge Edge = Edge.None, string? DamageType = null);

/// <summary>
/// The passive a hook's effects belong to: whose it is and its id, and for a zone's passive the zone's serial
/// (<see cref="Zone"/>).
/// </summary>
public sealed record PassiveRef(UnitId Owner, string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Zone = null);

/// <summary>
/// Fires passives on hooks (spec §4.4). A passive fires when its <see cref="PassiveDef.Hook"/> matches, its limits allow
/// it, it has a target (<see cref="PassiveDef.Target"/>) and its <see cref="PassiveDef.When"/> holds; its effects then run
/// right away: they go to the front of the queue, ahead of whatever was already waiting. Its limits are spent when the
/// effects start to run, not when it fires.
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
    /// then its conditions' passives in the order the conditions were applied, then the passives of the zones it stands in
    /// (<see cref="ZoneRules"/>), whose effects come from the zone's owner. Each passive that fires queues a
    /// <see cref="RunEffectsStep"/> that spends its limits (once per turn, once per round, per-battle uses), emits
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

        foreach (var (passive, source, zone) in Candidates(w.State, owner, rules).Where(c => c.Passive.Hook == hook).ToArray())
        {
            if (passive.Effects.IsDefaultOrEmpty || !Available(w.Unit(owner.Id), passive))
                continue;
            UnitId target;
            if (passive.Target == PassiveTarget.Self)
                target = owner.Id;
            else if (args.Other is { } other && other != owner.Id)
                target = other;
            else
                continue;
            if (passive.When is { } when && !Holds(when, w, args, rules))
                continue;

            var depth = w.HookDepth + 1;
            if (depth > MaxDepth)
                throw new RulesInvariantException(
                    $"Hooks nested deeper than {MaxDepth}: '{passive.Id}' of {owner.Id} on {hook}.");
            into.Add(new RunEffectsStep(source, target, args.Tile ?? w.Unit(target).Pos, null, passive.Effects, args.Roll,
                HookDepth: depth, Passive: new PassiveRef(owner.Id, passive.Id, zone)));
        }
    }

    /// <summary>
    /// Spends the limits of the passive <paramref name="passive"/> names as its effects start to run: false (nothing
    /// spent) when its owner no longer has it or its limits are used up. A zone's passive needs the zone to be still there,
    /// not the owner to be still in it (a unit pushed through a fire zone is burnt).
    /// </summary>
    internal static bool TrySpend(BattleWriter w, PassiveRef passive, RulesPack rules)
    {
        if (!w.State.Units.TryGetValue(passive.Owner, out var owner))
            return false;
        var def = passive.Zone is { } serial
            ? w.State.Zones.FirstOrDefault(z => z.Serial == serial)?.Passives.FirstOrDefault(p => p.Id == passive.Id)
            : Candidates(w.State, owner, rules).Where(c => c.Zone is null).Select(c => c.Passive).FirstOrDefault(p => p.Id == passive.Id);
        if (def is null || !Available(owner, def))
            return false;
        w.Set(Spend(owner, def));
        return true;
    }

    /// <summary>
    /// The owner's passives with the unit their effects come from: sheet passives in sheet order (from the owner), then its
    /// conditions' passives in the order the conditions were applied (from the condition's source while that unit is in
    /// the battle's roster, else the owner), then the passives of the zones it stands in, in serial order (from the zone's
    /// owner while that unit is in the roster, else the owner; with the zone's serial).
    /// </summary>
    private static IEnumerable<(PassiveDef Passive, UnitId Source, int? Zone)> Candidates(BattleState state, UnitState owner,
        RulesPack rules)
    {
        foreach (var passive in Modifiers.Passives(owner, rules))
            yield return (passive, owner.Id, null);
        foreach (var (def, from) in Conditions.Active(owner, rules))
        {
            var source = from.Source is { } s && state.Units.ContainsKey(s) ? s : owner.Id;
            foreach (var passive in def.Passives.IsDefault ? [] : def.Passives)
                yield return (passive, source, null);
        }
        foreach (var (passive, zone) in ZoneRules.PassivesAt(state, owner))
        {
            var source = zone.Owner is { } s && state.Units.ContainsKey(s) ? s : owner.Id;
            yield return (passive, source, zone.Serial);
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
        if (when.Success is { } success && (args.Roll?.Success == true) != success)
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
