using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// <c>"attack"</c>: an attack roll against the target. Fires the attacker's <see cref="HookKind.BeforeAttackRoll"/> hooks,
/// whose effects resolve first, then queues an <see cref="AttackStep"/> that rolls and resolves the attack.
/// </summary>
public sealed class AttackBlock : EffectBlock<AttackEffect>
{
    /// <inheritdoc />
    public override string Key => "attack";

    /// <inheritdoc />
    protected override void Run(AttackEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        if (def.Kind is not ("weapon" or "spell"))
            throw new InvalidOperationException($"'attack' kind must be \"weapon\" or \"spell\", not '{def.Kind}'.");
        ctx.W.PushFront(new AttackStep(ctx.Source, target.Id, ctx.Aim, ctx.Ability?.Id, def, HookDepth: ctx.W.HookDepth));
        Hooks.Fire(ctx.W, HookKind.BeforeAttackRoll,
            new HookArgs(ctx.Source, target.Id, Tile: target.Pos, Ability: ctx.Ability?.Id, AttackKind: def.Kind), ctx.Rules);
    }

    /// <summary>The stat a weapon attacks (and deals damage) with: the better of STR and DEX for a finesse weapon.</summary>
    public static Stat WeaponStat(UnitSheet sheet)
        => sheet.Weapon.Finesse && sheet.Score(Stat.Dex) > sheet.Score(Stat.Str) ? Stat.Dex
            : sheet.Weapon.Finesse ? Stat.Str
            : sheet.Weapon.Stat;
}

/// <summary>
/// An attack roll in two runs (spec §3.4, §3.6). Nothing happens if the target is Dead or Out, or if it is an ability's
/// attack (<see cref="HookDepth"/> 0) and its source is no longer Up. Each attack is its own hit: the first run numbers it
/// (<see cref="Resolution"/>), and its branch and the <see cref="HookKind.OnAttackHit"/>/<see cref="HookKind.OnAttackMiss"/>
/// hook effects keep the number, so a Downed target loses countdown once for it.
/// <list type="number">
/// <item>Without <see cref="Roll"/>: d20 + stat modifier (the weapon's, or the casting stat for <c>"spell"</c>; the
/// effect's stat overrides either) + proficiency + the effect's bonus + static attack modifiers + high ground, against AC +
/// static AC modifiers + cover (ranged and spell attacks, from <see cref="Sight.Trace"/>). The edge combines the attacker's
/// own-attack edges, the target's attacked-in-melee or attacked-at-range edges, invisibility on either side, flanking
/// (melee), a taunt drawing the attacker to someone else (<see cref="TauntBlock"/>) and help (<see cref="HelpBlock.Consume"/>,
/// used up by the roll). Emits <see cref="Rolled"/>; on a hit, opens a
/// <see cref="ReactionTriggerKind.BeforeHit"/> window whose <see cref="OfferReactionStep"/>s run before this step comes back
/// with the roll.</item>
/// <item>With <see cref="Roll"/>: the hit is re-judged against the target's AC now (a reaction may have raised it), with
/// the same <see cref="Cover"/>; a natural 20 still hits and a natural 1 still misses. A hit on a target with
/// <see cref="StaticMods.CritWhenAdjacent"/> from an adjacent attacker is a critical hit. Emits
/// <see cref="AttackResolved"/>, fires
/// <see cref="HookKind.OnAttackHit"/> or <see cref="HookKind.OnAttackMiss"/> for the attacker and queues the branch
/// (hit, plus crit; or miss) ahead of the hook effects.</item>
/// </list>
/// </summary>
public sealed record AttackStep(UnitId Source, UnitId Target, Tile? Aim, AbilityId? Ability, EffectDef Attack,
    RollResult? Roll = null, int Cover = 0, Edge Edge = Edge.None, int HookDepth = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Resolution = null) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var target = w.Unit(Target);
        if (target.Life is LifeState.Dead or LifeState.Out || (HookDepth == 0 && w.Unit(Source).Life != LifeState.Up))
            return;
        w.HookDepth = HookDepth;
        if (Roll is null)
            RollAttack(w, rules, target);
        else
            Resolve(w, rules, target, Roll);
    }

    private void RollAttack(BattleWriter w, RulesPack rules, UnitState target)
    {
        var def = (AttackEffect)Attack;
        var source = w.Unit(Source);
        var ability = Ability is { } id ? rules.Abilities.GetValueOrDefault(id) : null;
        var weapon = def.Kind == "weapon";
        var ranged = weapon ? source.Sheet.Weapon.Ranged : (ability?.Target.Range ?? 1) > 1;

        var statModifier = def.Stat is { } stat ? Stats.Mod(source.Sheet.Score(stat))
            : weapon ? Stats.Mod(source.Sheet.Score(AttackBlock.WeaponStat(source.Sheet)))
            : EffectContext.CastingModifierOf(source.Sheet);
        var attackerSources = Modifiers.Sources(w.State, Source, rules).ToArray();
        var targetSources = Modifiers.Sources(w.State, Target, rules).ToArray();
        var modifier = statModifier + Stats.Proficiency(source.Sheet.Level) + def.Bonus
                       + Modifiers.Sum(attackerSources).Attack + Reach.HighGroundBonus(w.Terrain, source.Pos, target.Pos);

        var (advantages, disadvantages) = (0, 0);
        void Count(Edge edge)
        {
            if (edge == Edge.Advantage)
                advantages++;
            else if (edge == Edge.Disadvantage)
                disadvantages++;
        }
        foreach (var mods in attackerSources)
        {
            Count(mods.OwnAttacks);
            Count(mods.Invisible ? Edge.Advantage : Edge.None);
        }
        foreach (var mods in targetSources)
        {
            Count(ranged ? mods.AttackedRanged : mods.AttackedMelee);
            Count(mods.Invisible ? Edge.Disadvantage : Edge.None);
        }
        if (!ranged && Modifiers.Flanked(w.State, Source, Target, rules))
            advantages++;
        if (TauntBlock.TauntedAway(w.State, Source, Target))
            disadvantages++;
        if (HelpBlock.Consume(w, Source))
            advantages++;
        var edge = Rolls.Combine(advantages, disadvantages);

        var cover = ranged || def.Kind == "spell" ? Modifiers.CoverAgainst(w, source.Pos, target.Pos, ranged: true) : 0;
        var roll = Rolls.D20(w, modifier, Ac(w, rules, target, cover), edge, critOn20: true, autoMissOn1: true);
        w.Emit(new Rolled(Source, RollKind.Attack, roll));

        var next = this with { Roll = roll, Cover = cover, Edge = edge, Resolution = Resolution ?? w.NewResolution() };
        if (!roll.Success)
        {
            w.PushFront(next);
            return;
        }
        var trigger = new ReactionTrigger(ReactionTriggerKind.BeforeHit, Source, Target, Ability, roll);
        w.PushFront([.. Reactions.OffersFor(w, trigger, rules).Select(o => new OfferReactionStep(o.Reactor, trigger)), next]);
    }

    private void Resolve(BattleWriter w, RulesPack rules, UnitState target, RollResult rolled)
    {
        var def = (AttackEffect)Attack;
        var source = w.Unit(Source);
        var ac = Ac(w, rules, target, Cover);
        var hit = !rolled.Fumble && (rolled.Crit || rolled.Total >= ac);
        var crit = hit && (rolled.Crit
                           || Modifiers.For(w, Target, rules).CritWhenAdjacent && Reach.Distance(source.Pos, target.Pos) == 1);
        var roll = rolled with { Vs = ac, Success = hit, Crit = crit };
        w.Emit(new AttackResolved(Source, Target, hit, crit));

        var hooks = new List<Step>();
        Hooks.Collect(w, hit ? HookKind.OnAttackHit : HookKind.OnAttackMiss,
            new HookArgs(Source, Target, roll, Tile: target.Pos, Ability: Ability, AttackKind: def.Kind, Edge: Edge), rules, hooks);

        var branch = !hit ? def.OnMiss
            : crit && !def.OnCrit.IsDefaultOrEmpty ? (def.OnHit.IsDefault ? def.OnCrit : def.OnHit.AddRange(def.OnCrit))
            : def.OnHit;
        for (var i = 0; i < hooks.Count; i++)
            if (hooks[i] is RunEffectsStep { Target: { } hookTarget } hook && hookTarget == Target)
                hooks[i] = hook with { Resolution = Resolution };   // the hit's own extra damage (sneak attack) is the same hit
        if (!branch.IsDefaultOrEmpty)
            hooks.Insert(0, new RunEffectsStep(Source, Target, Aim, Ability, branch, roll, HookDepth: HookDepth,
                Resolution: Resolution));
        if (hooks.Count > 0)
            w.PushFront([.. hooks]);
    }

    /// <summary>The target's AC now: the sheet's, plus static AC modifiers, plus <paramref name="cover"/>.</summary>
    private static int Ac(BattleWriter w, RulesPack rules, UnitState target, int cover)
        => target.Sheet.Ac + Modifiers.For(w, target.Id, rules).Ac + cover;
}

/// <summary>
/// <c>"save"</c>: the target rolls a saving throw (<see cref="Rolls.Save"/>) against the DC; a DEX save adds the cover the
/// target has from the effect's origin. Queues the fail branch, or on success the half damage and the success branch.
/// </summary>
public sealed class SaveBlock : EffectBlock<SaveEffect>
{
    /// <inheritdoc />
    public override string Key => "save";

    /// <inheritdoc />
    protected override void Run(SaveEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var cover = def.Stat == Stat.Dex ? Modifiers.CoverAgainst(ctx.W, Origin(ctx), target.Pos, ranged: false) : 0;
        var roll = Rolls.Save(ctx.W, target.Id, def.Stat, def.Dc ?? ctx.SaveDc, ctx.Rules, cover, ctx.Source);
        ctx.Last = roll;

        if (!roll.Success)
        {
            ctx.Queue(def.OnFail, roll, saveStat: def.Stat);
            return;
        }

        // Each Queue goes to the front, so queue the success branch first for the half damage to run before it.
        ctx.Queue(def.OnSuccess, roll, saveStat: def.Stat);
        if (def.HalfOnSuccess && !def.OnFail.IsDefault)
            ctx.Queue([.. def.OnFail.OfType<DamageEffect>()], roll, halfDamage: true, saveStat: def.Stat);
    }

    /// <summary>Where a save's effect comes from, for cover: the aimed tile of a burst, cross or ring, else the source.</summary>
    private static Tile Origin(EffectContext ctx)
        => ctx.Ability?.Target.Area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && ctx.Aim is { } aim
            ? aim
            : ctx.SourceUnit.Pos;
}
