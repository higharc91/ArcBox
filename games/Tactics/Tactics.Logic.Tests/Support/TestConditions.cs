using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>SRD conditions (and burning) built in C# for tests, with helpers to put them on units.</summary>
public static class TestConditions
{
    /// <summary>Melee attacks against it have advantage, ranged ones disadvantage; its own attacks have disadvantage.</summary>
    public static readonly ConditionDef Prone = new(new ConditionId("prone"), "Prone",
        new StaticMods(OwnAttacks: Edge.Disadvantage, AttackedMelee: Edge.Advantage, AttackedRanged: Edge.Disadvantage), [],
        Ordinal.Set("physical"));

    /// <summary>No actions, bonus actions or reactions.</summary>
    public static readonly ConditionDef Incapacitated = new(new ConditionId("incapacitated"), "Incapacitated",
        new StaticMods(Incapacitated: true), [], Ordinal.Set());

    /// <summary>Incapacitated; fails STR and DEX saves; attacks against it have advantage.</summary>
    public static readonly ConditionDef Stunned = new(new ConditionId("stunned"), "Stunned",
        new StaticMods(AutoFailSaves: [Stat.Str, Stat.Dex], AttackedMelee: Edge.Advantage, AttackedRanged: Edge.Advantage), [],
        Ordinal.Set("magical"), Implies: Incapacitated.Id);

    /// <summary>Can't move; its attacks have disadvantage and attacks against it advantage.</summary>
    public static readonly ConditionDef Restrained = new(new ConditionId("restrained"), "Restrained",
        new StaticMods(CantMove: true, OwnAttacks: Edge.Disadvantage, AttackedMelee: Edge.Advantage, AttackedRanged: Edge.Advantage), [],
        Ordinal.Set("physical"));

    /// <summary>Unseen: its attacks have advantage, attacks against it disadvantage.</summary>
    public static readonly ConditionDef Invisible = new(new ConditionId("invisible"), "Invisible",
        new StaticMods(Invisible: true), [], Ordinal.Set("magical"));

    /// <summary>1d6 fire at the start of each of its turns.</summary>
    public static readonly ConditionDef Burning = new(new ConditionId("burning"), "Burning", null,
        [new PassiveDef("burning.tick", HookKind.OnTurnStart, null, [new DamageEffect(TestAbilities.Flat("1d6"), "fire", [])], null,
            Target: PassiveTarget.Self)],
        Ordinal.Set("physical"));

    /// <summary>Every condition above.</summary>
    public static readonly ImmutableArray<ConditionDef> All = [Prone, Incapacitated, Stunned, Restrained, Invisible, Burning];

    /// <summary>A condition with no duration and no source.</summary>
    public static ActiveCondition Lasting(ConditionDef condition, UnitId? source = null, int? rounds = null)
        => new(condition.Id, source, rounds, UntilSave: false, SaveDc: 0, Stat.Con, Concentration: false);

    /// <summary>The state with <paramref name="conditions"/> added to <paramref name="unit"/>.</summary>
    public static BattleState Give(this BattleState state, string unit, params ActiveCondition[] conditions)
    {
        var u = state.Units[new UnitId(unit)];
        return state with { Units = state.Units.SetItem(u.Id, u with { Conditions = u.Conditions.AddRange(conditions) }) };
    }

    /// <summary>The state with <paramref name="unit"/> changed by <paramref name="change"/>.</summary>
    public static BattleState Change(this BattleState state, string unit, Func<UnitState, UnitState> change)
    {
        var u = state.Units[new UnitId(unit)];
        return state with { Units = state.Units.SetItem(u.Id, change(u)) };
    }

    /// <summary>A longbow (range 8, DEX) for archer sheets.</summary>
    public static readonly WeaponDef Longbow = new("longbow", Dice.Parse("1d8"), "piercing", Stat.Dex, Finesse: false,
        Ranged: true, Range: 8, Reach: false, Tags: Ordinal.Set());

    /// <summary>The first attack roll in <paramref name="events"/>.</summary>
    public static RollResult FirstAttack(ImmutableArray<BattleEvent> events)
        => events.OfType<Rolled>().First(r => r.Kind == RollKind.Attack).Result;
}
