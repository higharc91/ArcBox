using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// One effect block's data, as written in content: <c>{"&lt;key&gt;": {...}}</c> with any branches beside the key (see
/// <see cref="EffectJsonConverter"/>). <see cref="BlockRegistry"/> maps each record type to the block that runs it.
/// </summary>
[JsonConverter(typeof(EffectJsonConverter))]
public abstract record EffectDef;

/// <summary>
/// <c>"attack"</c>: an attack roll against the target's AC. <see cref="Kind"/> is <c>"weapon"</c> (the sheet's weapon stat,
/// the better of STR and DEX for a finesse weapon) or <c>"spell"</c> (the caster's <see cref="UnitSheet.CastingStat"/>);
/// <see cref="Stat"/> overrides either. A hit runs <see cref="OnHit"/>, a critical hit also <see cref="OnCrit"/>, and a miss
/// <see cref="OnMiss"/>.
/// </summary>
public sealed record AttackEffect(string Kind, Stat? Stat, ImmutableArray<EffectDef> OnHit,
    ImmutableArray<EffectDef> OnMiss, ImmutableArray<EffectDef> OnCrit, int Bonus = 0) : EffectDef;

/// <summary>
/// <c>"save"</c>: the target rolls a saving throw against <see cref="Dc"/>, or 8 + proficiency + the caster's casting
/// modifier. A failure runs <see cref="OnFail"/>; a success runs <see cref="OnSuccess"/>, after the damage blocks of
/// <see cref="OnFail"/> at half (rounded down) when <see cref="HalfOnSuccess"/> is set.
/// </summary>
public sealed record SaveEffect(Stat Stat, int? Dc, ImmutableArray<EffectDef> OnFail,
    ImmutableArray<EffectDef> OnSuccess, bool HalfOnSuccess = false) : EffectDef;

/// <summary>
/// <c>"damage"</c>: <see cref="Dice"/> at the user's level plus the named <see cref="Bonus"/> addends, of
/// <see cref="Type"/>. With <see cref="Weapon"/>, the sheet's weapon dice and attack stat are added, and the type defaults
/// to the weapon's. A critical hit rolls the dice twice.
/// </summary>
public sealed record DamageEffect(ByLevel<Dice>? Dice, string? Type, ImmutableArray<string> Bonus,
    bool Weapon = false) : EffectDef;

/// <summary><c>"heal"</c>: restores <see cref="Dice"/> at the user's level plus the named <see cref="Bonus"/> addends, up to max HP.</summary>
public sealed record HealEffect(ByLevel<Dice> Dice, ImmutableArray<string> Bonus) : EffectDef;

/// <summary>
/// <c>"apply"</c>: gives the target a condition for <see cref="Rounds"/> rounds (null: until removed), or until it saves
/// at the end of its turn (<see cref="UntilSave"/>, which needs an enclosing <c>"save"</c> for its stat and DC).
/// JSON shorthand: <c>{"apply": "lit", "rounds": 1}</c>.
/// </summary>
[EffectShorthand(nameof(Condition))]
public sealed record ApplyEffect(ConditionId Condition, int? Rounds, bool UntilSave = false) : EffectDef;

/// <summary><c>"remove"</c>: ends every instance of a condition on the target. JSON shorthand: <c>{"remove": "prone"}</c>.</summary>
[EffectShorthand(nameof(Condition))]
public sealed record RemoveEffect(ConditionId Condition) : EffectDef;

/// <summary>
/// <c>"push"</c>: moves the target <see cref="Distance"/> tiles, one at a time, straight away from the source (or, without
/// <see cref="AwayFromSource"/>, away from the aimed tile) along the dominant axis. See <see cref="Motion.Push"/>.
/// </summary>
public sealed record PushEffect(int Distance, bool AwayFromSource = true) : EffectDef;

/// <summary><c>"pull"</c>: moves the target up to <see cref="Distance"/> tiles toward the source, stopping beside it.</summary>
public sealed record PullEffect(int Distance) : EffectDef;

/// <summary>
/// <c>"teleport"</c>: moves the source (or, without <see cref="Self"/>, the target) to the aimed tile, which must be
/// walkable, empty and within <see cref="Range"/> tiles of the unit; otherwise nothing happens.
/// </summary>
public sealed record TeleportEffect(int Range, bool Self = true) : EffectDef;

/// <summary><c>"swap"</c>: the source and the target exchange tiles.</summary>
public sealed record SwapEffect : EffectDef;

/// <summary>
/// <c>"dash"</c>: the source walks up to <see cref="Tiles"/> tiles toward the aimed tile, stopping before anything it can't
/// step onto or any unit. It spends no Move and provokes nothing.
/// </summary>
public sealed record DashEffect(int Tiles) : EffectDef;

/// <summary>
/// <c>"jumpTo"</c>: the source leaps to the aimed tile, ignoring its Jump limit, if the tile is walkable, empty and within
/// <see cref="Range"/> tiles; otherwise nothing happens.
/// </summary>
public sealed record JumpToEffect(int Range) : EffectDef;
