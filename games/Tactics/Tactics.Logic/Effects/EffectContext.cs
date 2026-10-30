using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Where an effect block runs: the writer, who uses the effect (<see cref="Source"/>) on whom (<see cref="Target"/>,
/// null for tile effects) and where (<see cref="Aim"/>), the ability, the roll the effect follows (<see cref="Last"/>) and
/// the user's level for <see cref="ByLevel{T}"/> tables.
/// </summary>
public sealed class EffectContext
{
    /// <summary>A context for running effects of <paramref name="ability"/> from <paramref name="source"/>.</summary>
    public EffectContext(BattleWriter w, RulesPack rules, UnitId source, UnitId? target, Tile? aim, AbilityDef? ability,
        RollResult? last = null, bool halfDamage = false, Stat? saveStat = null)
    {
        W = w;
        Rules = rules;
        Source = source;
        Target = target;
        Aim = aim;
        Ability = ability;
        Last = last;
        HalfDamage = halfDamage;
        SaveStat = saveStat;
        Level = w.Unit(source).Sheet.Level;
    }

    /// <summary>The battle's working copy.</summary>
    public BattleWriter W { get; }

    /// <summary>The unit using the effect.</summary>
    public UnitId Source { get; }

    /// <summary>The unit the effect lands on, or null for an effect on a tile.</summary>
    public UnitId? Target { get; }

    /// <summary>The tile the ability was aimed at.</summary>
    public Tile? Aim { get; }

    /// <summary>The ability the effect belongs to, if any.</summary>
    public AbilityDef? Ability { get; }

    /// <summary>The roll this effect follows (the attack or save whose branch is running), if any.</summary>
    public RollResult? Last { get; set; }

    /// <summary>The source's level, which picks the step of every <see cref="ByLevel{T}"/> table.</summary>
    public int Level { get; }

    /// <summary>The content the battle runs under.</summary>
    public RulesPack Rules { get; }

    /// <summary>True while a successful save's half damage runs: damage blocks halve their total, rounding down.</summary>
    public bool HalfDamage { get; }

    /// <summary>The stat of the save whose branch is running, if any; an until-save condition rolls it each turn end.</summary>
    public Stat? SaveStat { get; }

    /// <summary>The source as it is now.</summary>
    public UnitState SourceUnit => W.Unit(Source);

    /// <summary>The target as it is now. Throws when the effect has no target unit.</summary>
    public UnitState TargetUnit(string block)
        => Target is { } id ? W.Unit(id) : throw new InvalidOperationException($"'{block}' needs a target unit.");

    /// <summary>The source's casting modifier: the modifier of its <see cref="UnitSheet.CastingStat"/>, or 0 without one.</summary>
    public int CastingModifier => CastingModifierOf(SourceUnit.Sheet);

    /// <summary>The source's save DC: 8 + proficiency + casting modifier.</summary>
    public int SaveDc => 8 + Stats.Proficiency(Level) + CastingModifier;

    /// <summary>
    /// The sum of named addends: <c>"mod.&lt;stat&gt;"</c> (e.g. <c>"mod.wis"</c>), <c>"mod.cast"</c>, <c>"prof"</c> and
    /// <c>"level"</c>, all of the source. Throws <see cref="InvalidOperationException"/> naming an unknown addend.
    /// </summary>
    public int Sum(ImmutableArray<string> addends)
    {
        var total = 0;
        foreach (var name in addends.IsDefault ? [] : addends)
            total += Addend(name);
        return total;
    }

    /// <summary>One named addend; see <see cref="Sum"/>.</summary>
    public int Addend(string name)
    {
        var sheet = SourceUnit.Sheet;
        switch (name)
        {
            case "prof":
                return Stats.Proficiency(Level);
            case "level":
                return Level;
            case "mod.cast":
                return CastingModifier;
        }
        if (name.StartsWith("mod.", StringComparison.Ordinal) && ParseStat(name[4..]) is { } stat)
            return Stats.Mod(sheet.Score(stat));
        throw new InvalidOperationException(
            $"Unknown addend '{name}' (expected \"mod.<stat>\", \"mod.cast\", \"prof\" or \"level\").");
    }

    /// <summary>Queues <paramref name="effects"/> on the same source, target and aim to run next, following <paramref name="last"/>.</summary>
    public void Queue(ImmutableArray<EffectDef> effects, RollResult? last, bool halfDamage = false, Stat? saveStat = null)
    {
        if (!effects.IsDefaultOrEmpty)
            W.PushFront(new RunEffectsStep(Source, Target, Aim, Ability?.Id, effects, last, halfDamage, saveStat));
    }

    /// <summary>The modifier of <paramref name="sheet"/>'s casting stat, or 0 without one.</summary>
    public static int CastingModifierOf(UnitSheet sheet)
        => sheet.CastingStat is { } name && ParseStat(name) is { } stat ? Stats.Mod(sheet.Score(stat)) : 0;

    /// <summary>A stat from its name, ignoring case (<c>"wis"</c>, <c>"Wis"</c>), or null.</summary>
    public static Stat? ParseStat(string name)
        => name.Length > 0 && name.All(char.IsLetter) && Enum.TryParse<Stat>(name, ignoreCase: true, out var stat)
            ? stat
            : null;
}
