using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Tooltip text generated from the content (spec §4.1 point 4), so a description can't drift from the rules: each effect
/// block supplies its own phrase (<see cref="IEffectBlock.Describe"/>) and this class assembles them. A block with no phrase
/// makes <see cref="For(AbilityDef, int, RulesPack)"/> throw <see cref="InvalidOperationException"/> naming its key, which is
/// how content tests catch a block or hook that can't be described.
/// <list type="bullet">
/// <item>Ability: <c>"{header}: {effects}"</c> with <c>" — {flavour}"</c> after. The header is
/// <c>"{Spell attack|Weapon attack|Dex save}, {range} tiles"</c> when the ability's first effect is an attack or a save (an
/// ability with no roll has just <c>"{range} tiles"</c>, or <c>"Self"</c> for a self target), then <c>", {shape} {size}"</c>
/// for an area (nothing for a single target or the caster). A range of 1 reads <c>"1 tile"</c>; the basic weapon attack
/// reads <c>"weapon range"</c>, as its reach is the weapon's. Effects are joined with "; ". A roll further down the
/// list is described inline: <c>"Con save (target is Stunned for 1 round)"</c>.</item>
/// <item>Durations: <c>"for 1 round"</c>, <c>"for N rounds"</c>, <c>"until the end of your turn"</c> (0 rounds),
/// <c>"until saved"</c>. Condition names come from the pack, else the capitalised id.</item>
/// <item>Passive: the hook (<c>"When you hit"</c>), <c>", if {when}"</c>, <c>", on yourself"</c> for a self target, then
/// the effects and the limits in brackets. Static modifiers come first.</item>
/// <item>Condition: its modifiers, an implied condition and its passives, worded for the unit that has it, with passives'
/// effects at level 1.</item>
/// </list>
/// </summary>
public static class Tooltips
{
    /// <summary>The tooltip for <paramref name="a"/> as used by a unit of <paramref name="level"/>.</summary>
    public static string For(AbilityDef a, int level, RulesPack rules)
    {
        var effects = a.Effects.IsDefault ? ImmutableArray<EffectDef>.Empty : a.Effects;
        var lead = effects.Length > 0 ? effects[0] : null;
        var roll = lead switch
        {
            AttackEffect attack => AttackBlock.Label(attack),
            SaveEffect save => SaveBlock.Label(save),
            _ => null,
        };

        var range = a.IsWeaponAttack ? "weapon range"
            : a.Target.Kind == TargetKind.Self ? "self"
            : TooltipText.Tiles(a.Target.Range);
        var header = roll is null ? TooltipText.Capitalise(range) : $"{TooltipText.Capitalise(roll)}, {range}";
        if (a.Target.Area.Shape is not (AreaShape.Single or AreaShape.Self))
            header += $", {TooltipText.Area(a.Target.Area)}";

        List<string> parts = [];
        for (var i = 0; i < effects.Length; i++)
            parts.Add(i == 0 && lead is AttackEffect or SaveEffect ? LeadBody(effects[0], level, rules) : Phrase(effects[i], level, rules));
        var text = parts.Count == 0 ? header : $"{header}: {string.Join("; ", parts)}";
        return string.IsNullOrEmpty(a.Flavor) ? text : $"{text} — {a.Flavor}";
    }

    /// <summary>The tooltip for <paramref name="p"/> as used by a unit of <paramref name="level"/>.</summary>
    public static string For(PassiveDef p, int level, RulesPack rules)
    {
        List<string> parts = [];
        parts.AddRange(TooltipText.Clauses(p.Static, rules));
        if (!p.Effects.IsDefaultOrEmpty)
        {
            var effects = List(p.Effects, level, rules);
            var limits = Limits(p);
            if (limits.Count > 0)
                effects += $" ({string.Join(", ", limits)})";
            var trigger = p.Hook is { } hook ? HookPhrase(hook) : null;
            if (trigger is not null && p.When is { } when)
                trigger += $", if {TooltipText.When(when, rules)}";
            if (trigger is not null && p.Target == PassiveTarget.Self)
                trigger += ", on yourself";
            parts.Add(trigger is null ? effects : $"{trigger}: {effects}");
        }
        return parts.Count == 0 ? "no listed effect" : string.Join("; ", parts);
    }

    /// <summary>The tooltip for <paramref name="c"/>: what it does to the unit that has it. Passives' effects are described at level 1.</summary>
    public static string For(ConditionDef c, RulesPack rules)
    {
        List<string> parts = [];
        parts.AddRange(TooltipText.Clauses(c.Static, rules));
        if (c.Implies is { } implied)
            parts.Add($"also counts as {TooltipText.Condition(implied, rules)}");
        if (!c.Passives.IsDefaultOrEmpty)
            parts.AddRange(c.Passives.Select(p => For(p, 1, rules)));
        return parts.Count == 0 ? "no listed effect" : string.Join("; ", parts);
    }

    /// <summary>The phrases of <paramref name="effects"/> joined with "; ". Blocks call this for the effects they hold.</summary>
    public static string List(ImmutableArray<EffectDef> effects, int level, RulesPack rules)
        => effects.IsDefaultOrEmpty ? "" : string.Join("; ", effects.Select(e => Phrase(e, level, rules)));

    /// <summary>Like <see cref="List"/>, in brackets when there is more than one phrase so it can sit inside a longer one.</summary>
    public static string Group(ImmutableArray<EffectDef> effects, int level, RulesPack rules)
        => effects.IsDefaultOrEmpty ? "nothing" : effects.Length == 1 ? List(effects, level, rules) : $"({List(effects, level, rules)})";

    /// <summary>The block's phrase for one effect; throws naming the block when it has none.</summary>
    private static string Phrase(EffectDef def, int level, RulesPack rules)
    {
        var block = BlockRegistry.For(def);
        return block.Describe(def, level, rules)
               ?? throw new InvalidOperationException($"The '{block.Key}' effect block has no tooltip phrase for {def}.");
    }

    /// <summary>The body of the roll a header names, without its label.</summary>
    private static string LeadBody(EffectDef roll, int level, RulesPack rules)
    {
        _ = BlockRegistry.For(roll);   // fail the same way as any other effect for a def no block runs
        return roll switch
        {
            AttackEffect attack => AttackBlock.Body(attack, level, rules),
            SaveEffect save => SaveBlock.Body(save, level, rules),
            _ => throw new ArgumentException("Only an attack or a save leads a header.", nameof(roll)),
        };
    }

    private static List<string> Limits(PassiveDef p)
    {
        List<string> limits = [];
        if (p.OncePerTurn)
            limits.Add("once per turn");
        if (p.OncePerRound)
            limits.Add("once per round");
        if (p.UsesPerBattle is { } uses)
            limits.Add(uses == 1 ? "once per battle" : $"{uses} times per battle");
        return limits;
    }

    private static string? HookPhrase(HookKind hook)
        => hook switch
        {
            HookKind.OnBattleStart => "At the start of battle",
            HookKind.OnRoundStart => "At the start of each round",
            HookKind.OnTurnStart => "At the start of your turn",
            HookKind.OnTurnEnd => "At the end of your turn",
            HookKind.BeforeAttackRoll => "Before you attack",
            HookKind.OnAttackHit => "When you hit",
            HookKind.OnAttackMiss => "When you miss",
            HookKind.OnDamaged => "When you take damage",
            HookKind.OnSave => "When you roll a save",
            HookKind.OnMove => "When you move",
            HookKind.OnEnterTile => "When you enter a tile",
            HookKind.OnDowned => "When you are downed",
            HookKind.OnKill => "When you kill a unit",
            HookKind.OnAllyDowned => "When an ally is downed",
            _ => null,
        };
}
