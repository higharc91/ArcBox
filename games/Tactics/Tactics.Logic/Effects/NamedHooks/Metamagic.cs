using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>
/// <c>metamagic</c> (args <c>{"kind": "twinned" | "extended" | "careful" | "quickened" | "distant"}</c>): shapes the
/// sorcerer's next spell (or extends active concentration), applying the matching <c>metamagic.&lt;kind&gt;</c> condition
/// until the end of the turn or until a spell uses it up.
/// </summary>
public sealed class Metamagic : INamedHook
{
    /// <summary>The allowed metamagic kinds.</summary>
    public static readonly ImmutableArray<string> Kinds = ["careful", "distant", "extended", "quickened", "twinned"];

    /// <summary>Twinned Spell: a single-target spell also hits a second valid target in range.</summary>
    public static readonly ConditionId Twinned = new("metamagic.twinned");

    /// <summary>Extended Spell: concentration duration is doubled.</summary>
    public static readonly ConditionId Extended = new("metamagic.extended");

    /// <summary>Careful Spell: allies in the area automatically succeed their saving throws.</summary>
    public static readonly ConditionId Careful = new("metamagic.careful");

    /// <summary>Quickened Spell: an Action spell can be cast using a Bonus action.</summary>
    public static readonly ConditionId Quickened = new("metamagic.quickened");

    /// <summary>Distant Spell: spell range increases by 2 tiles.</summary>
    public static readonly ConditionId Distant = new("metamagic.distant");

    private static readonly ImmutableSortedSet<ConditionId> AllConditions =
        [Twinned, Extended, Careful, Quickened, Distant];

    /// <inheritdoc />
    public string Name => "metamagic";

    /// <inheritdoc />
    public string? Describe(JsonElement args, RulesPack rules)
        => NamedHooks.String(args, "kind") switch
        {
            "twinned" => "your next single-target spell this turn also hits a second target in range",
            "extended" => "doubles the duration of your concentration spell",
            "careful" => "allies in the area of your next spell this turn automatically succeed their saves",
            "quickened" => "your next Action spell this turn costs no Action (uses your Bonus action)",
            "distant" => "your next spell this turn has +2 range",
            _ => null,
        };

    /// <inheritdoc />
    public void Run(JsonElement args, EffectContext ctx)
    {
        var kind = NamedHooks.String(args, "kind")
                   ?? throw new InvalidOperationException("'metamagic' needs a \"kind\" in its args.");
        if (!Kinds.Contains(kind))
            throw new InvalidOperationException($"Unknown metamagic kind '{kind}'.");

        var unit = ctx.SourceUnit;
        if (kind == "quickened" && (ctx.Ability?.Cost.Bonus ?? 0) == 0)
        {
            ctx.W.Set(unit with { Economy = unit.Economy with { Bonus = false } });
            unit = ctx.SourceUnit;
        }
        if (kind == "extended" && ExtendActiveConcentration(ctx.W, unit.Id))
            return;

        var id = new ConditionId("metamagic." + kind);
        var condition = new ActiveCondition(id, ctx.Source, RoundsLeft: 0, UntilSave: false, SaveDc: 0, Stat.Con,
            Concentration: false, AppliedThisTurn: false);
        ctx.W.Set(unit with { Conditions = unit.Conditions.RemoveAll(c => c.Id == id).Add(condition) });
        ctx.W.Emit(new ConditionApplied(unit.Id, id));
    }

    /// <inheritdoc />
    public IEnumerable<string> Monsters(JsonElement args) => [];

    /// <inheritdoc />
    public void Validate(JsonElement args, Action<string> error)
    {
        var kind = NamedHooks.String(args, "kind");
        if (kind is null)
            error("'metamagic' needs a \"kind\" in its args.");
        else if (!Kinds.Contains(kind))
            error($"Unknown metamagic kind '{kind}' (expected \"careful\", \"distant\", \"extended\", \"quickened\" or \"twinned\").");
    }

    /// <summary>True when <paramref name="ability"/> is a metamagic activation ability itself.</summary>
    public static bool IsMetamagicAbility(AbilityDef ability)
        => !ability.Effects.IsDefaultOrEmpty
           && ability.Effects.Any(e => e is TransformEffect { Hook: "metamagic" });

    /// <summary>
    /// True when metamagic can change <paramref name="ability"/>: a spell or class ability, not a weapon attack, a metamagic
    /// ability itself, or one of the actions every unit has (<c>common.*</c>: Dash, Shove, Help, using an item, …).
    /// </summary>
    public static bool Shapes(AbilityDef ability)
        => !ability.IsWeaponAttack && !IsMetamagicAbility(ability) && !ability.Id.Value.StartsWith("common.", StringComparison.Ordinal);

    /// <summary>True when <paramref name="ability"/> is a Quickened metamagic ability with <c>Cost.Bonus == 0</c>.</summary>
    public static bool IsFreeQuickened(AbilityDef ability)
        => ability.Cost.Bonus == 0 && !ability.Effects.IsDefaultOrEmpty
           && ability.Effects.Any(e => e is TransformEffect t && t.Hook == "metamagic"
                                       && NamedHooks.String(t.Args, "kind") == "quickened");

    /// <summary>True when <paramref name="unit"/> has the given metamagic condition active.</summary>
    public static bool Has(UnitState unit, ConditionId metamagic)
        => !unit.Conditions.IsDefault && unit.Conditions.Any(c => c.Id == metamagic);

    /// <summary>True when <paramref name="unit"/> can pay an Action spell's action cost via Quickened Spell.</summary>
    public static bool Quickens(UnitState unit, AbilityDef ability)
        => ability.Cost.Action > 0 && Shapes(ability) && Has(unit, Quickened);

    /// <summary>Doubles <paramref name="rounds"/> when <paramref name="ctx"/> is a concentration effect under Extended Spell.</summary>
    public static int? ExtendRounds(EffectContext ctx, int? rounds, bool concentration)
        => concentration && rounds is { } r && Has(ctx.SourceUnit, Extended) ? r * 2 : rounds;

    /// <summary>Removes any active <c>metamagic.*</c> conditions from <paramref name="unit"/> once a spell finishes.</summary>
    public static void ConsumeAll(BattleWriter w, UnitId unit)
    {
        var u = w.Unit(unit);
        var active = u.Conditions.Where(c => AllConditions.Contains(c.Id)).Select(c => c.Id).ToArray();
        foreach (var id in active)
            Conditions.Remove(w, unit, id);
    }

    private static bool ExtendActiveConcentration(BattleWriter w, UnitId caster)
    {
        var extended = false;
        foreach (var unit in w.State.Units.Values)
        {
            if (!unit.Conditions.Any(c => c.Concentration && c.Source == caster && c.RoundsLeft is not null))
                continue;
            var updated = unit.Conditions.Select(c =>
                c.Concentration && c.Source == caster && c.RoundsLeft is { } r ? c with { RoundsLeft = r * 2 } : c)
                .ToImmutableArray();
            w.Set(unit with { Conditions = updated });
            extended = true;
        }
        if (w.State.Zones.Any(z => z.Concentration && z.Owner == caster && z.RoundsLeft is not null))
        {
            var zones = w.State.Zones.Select(z =>
                z.Concentration && z.Owner == caster && z.RoundsLeft is { } r ? z with { RoundsLeft = r * 2 } : z)
                .ToImmutableArray();
            w.State = w.State with { Zones = zones };
            extended = true;
        }
        return extended;
    }
}
