using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// What using an ability spends: Action, Bonus action, Reaction, Move (tiles), class resources by name, a per-battle use
/// and whether it needs the caster's concentration. Zero and absent fields cost nothing and are left out of the JSON.
/// </summary>
public sealed record CostDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Action = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Bonus = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Reaction = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Move = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedDictionary<string, int>? Resource = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int? Uses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Concentration = false);

/// <summary>Who or what an ability may be aimed at. JSON: <c>"self"</c>, <c>"ally"</c>, <c>"enemy"</c>, <c>"any_unit"</c>, ….</summary>
public enum TargetKind
{
    /// <summary>The user.</summary>
    [JsonStringEnumMemberName("self")] Self,
    /// <summary>A standing unit on the user's side.</summary>
    [JsonStringEnumMemberName("ally")] Ally,
    /// <summary>A standing unit on another side.</summary>
    [JsonStringEnumMemberName("enemy")] Enemy,
    /// <summary>Any standing unit.</summary>
    [JsonStringEnumMemberName("any_unit")] AnyUnit,
    /// <summary>A Downed unit on the user's side.</summary>
    [JsonStringEnumMemberName("downed_ally")] DownedAlly,
    /// <summary>A tile on the map.</summary>
    [JsonStringEnumMemberName("tile")] Tile,
    /// <summary>A walkable tile with no unit on it.</summary>
    [JsonStringEnumMemberName("empty_tile")] EmptyTile,
}

/// <summary>
/// How an ability is aimed: the kind of target, the range in tiles (plus the high-ground bonus), whether line of sight is
/// needed, the area it affects, a minimum range and, for melee, the largest height difference it reaches across.
/// </summary>
public sealed record TargetDef(TargetKind Kind, int Range, bool Los, AreaDef Area,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MinRange = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int? HeightTolerance = null);

/// <summary>Keys into the art manifests for showing an ability: the rig animation, the effect and the projectile.</summary>
public sealed record PresentationDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Anim,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Fx,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Projectile);

/// <summary>Hints for the AI planner, such as <c>"damage"</c>, <c>"single"</c> or <c>"heal"</c>.</summary>
public sealed record AiHints(ImmutableArray<string> Tags);

/// <summary>
/// An ability from the content: what it costs, how it is aimed and the effect blocks it runs on each target.
/// <see cref="IsWeaponAttack"/> marks the basic Attack action, which uses the sheet's weapon for range and damage and
/// repeats its effects <see cref="UnitSheet.Attacks"/> times. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record AbilityDef(AbilityId Id, string Name, int Tier, CostDef Cost, TargetDef Target,
    ImmutableArray<EffectDef> Effects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PresentationDef? Presentation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] AiHints? Ai = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? Flavor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] AbilityId? Replaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsWeaponAttack = false);

/// <summary>
/// When a passive's effects run (spec §4.4). JSON: <c>"onBattleStart"</c>, <c>"onAttackHit"</c>, …. Each hook fires for one
/// owner (<see cref="HookArgs.Owner"/>); the battle, round and turn hooks fire for every unit, in sorted
/// <see cref="UnitId"/> order. (<see cref="OnDowned"/>, <see cref="OnKill"/> and <see cref="OnAllyDowned"/> are fired by the
/// Downed rules, Task 8.)
/// </summary>
public enum HookKind
{
    /// <summary>The battle has started (every unit, before the first turn's effects).</summary>
    [JsonStringEnumMemberName("onBattleStart")] OnBattleStart,
    /// <summary>A round has started (every unit).</summary>
    [JsonStringEnumMemberName("onRoundStart")] OnRoundStart,
    /// <summary>The owner's turn has started (not when a delayed turn resumes).</summary>
    [JsonStringEnumMemberName("onTurnStart")] OnTurnStart,
    /// <summary>The owner's turn is ending, before its until-save rolls and duration ticks.</summary>
    [JsonStringEnumMemberName("onTurnEnd")] OnTurnEnd,
    /// <summary>The owner is about to make an attack roll against <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("beforeAttackRoll")] BeforeAttackRoll,
    /// <summary>The owner's attack hit <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("onAttackHit")] OnAttackHit,
    /// <summary>The owner's attack missed <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("onAttackMiss")] OnAttackMiss,
    /// <summary>The owner took damage (more than 0) from <see cref="HookArgs.Other"/>, if anyone.</summary>
    [JsonStringEnumMemberName("onDamaged")] OnDamaged,
    /// <summary>The owner rolled a saving throw.</summary>
    [JsonStringEnumMemberName("onSave")] OnSave,
    /// <summary>The owner walked one step (or dashed).</summary>
    [JsonStringEnumMemberName("onMove")] OnMove,
    /// <summary>The owner arrived on a tile by any means.</summary>
    [JsonStringEnumMemberName("onEnterTile")] OnEnterTile,
    /// <summary>The owner was Downed.</summary>
    [JsonStringEnumMemberName("onDowned")] OnDowned,
    /// <summary>The owner dropped <see cref="HookArgs.Other"/>.</summary>
    [JsonStringEnumMemberName("onKill")] OnKill,
    /// <summary>An ally of the owner was Downed.</summary>
    [JsonStringEnumMemberName("onAllyDowned")] OnAllyDowned,
}

/// <summary>
/// Modifiers that hold for as long as their passive or condition does (spec §4.4 <c>"static"</c>). Numbers add up across
/// sources; flags and sets combine; each edge is one source of advantage or disadvantage (see <see cref="Rolls.Combine"/>).
/// <list type="bullet">
/// <item><see cref="Ac"/>, <see cref="Attack"/> (attack rolls), <see cref="Damage"/> (weapon damage), <see cref="Move"/>,
/// <see cref="Initiative"/> and <see cref="Saves"/> (by stat) add to the unit's numbers.</item>
/// <item><see cref="Incapacitated"/>: no actions, bonus actions or reactions, and no flanking. <see cref="CantMove"/>: no
/// walking or dashing.</item>
/// <item><see cref="OwnAttacks"/>: the edge on the unit's attack rolls; <see cref="AttackedMelee"/> and
/// <see cref="AttackedRanged"/>: the edge on attack rolls against it. <see cref="Invisible"/> gives its attacks advantage
/// and attacks against it disadvantage.</item>
/// <item><see cref="AutoFailSaves"/>: saves of these stats fail whatever the roll. <see cref="CritWhenAdjacent"/>: a hit
/// from an adjacent attacker is a critical hit.</item>
/// <item><see cref="Resist"/> and <see cref="Immune"/> add damage types to the sheet's; <see cref="ExtraActions"/> adds
/// actions to each turn's economy.</item>
/// </list>
/// </summary>
public sealed record StaticMods(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Ac = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Attack = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Damage = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Move = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Initiative = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableSortedDictionary<Stat, int>? Saves = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Incapacitated = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CantMove = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge OwnAttacks = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge AttackedMelee = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Edge AttackedRanged = Edge.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableSortedSet<Stat>? AutoFailSaves = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CritWhenAdjacent = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Invisible = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedSet<string>? Resist = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonConverter(typeof(OrdinalJsonConverter))]
    ImmutableSortedSet<string>? Immune = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ExtraActions = 0)
{
    /// <summary>No modifiers.</summary>
    public static StaticMods None { get; } = new();
}

/// <summary>
/// A test on a hook's situation (see <see cref="Hooks.Holds"/>). JSON: an object with one key, either a combinator
/// (<c>{"all": [...]}</c>, <c>{"any": [...]}</c>, <c>{"not": {...}}</c>) or a leaf. "The target" is
/// <see cref="HookArgs.Other"/>, or the owner when there is none. Leaves:
/// <list type="bullet">
/// <item><see cref="Weapon"/>: a weapon attack whose weapon is any of these (<c>"finesse"</c>, <c>"ranged"</c>,
/// <c>"melee"</c>, <c>"reach"</c>, its id or one of its tags).</item>
/// <item><see cref="Advantage"/>: whether the roll had advantage. <see cref="Flanked"/>: whether the owner flanks the target
/// (an ally that can act stands directly opposite).</item>
/// <item><see cref="TargetHasCondition"/>, <see cref="TargetTag"/>: the target has the condition (implied ones included) or
/// the sheet tag. <see cref="HpBelow"/>: the target's HP is below this percentage of its max.</item>
/// <item><see cref="HeightAbove"/>: the owner stands at least this many levels above the target.</item>
/// <item><see cref="IsSpell"/>: whether the ability is a spell (tier 1 or more) or the attack a spell attack.</item>
/// <item><see cref="DamageType"/>: the damage taken was of this type.</item>
/// </list>
/// Every key present must hold.
/// </summary>
public sealed record WhenDef(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<WhenDef> All = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<WhenDef> Any = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WhenDef? Not = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<string> Weapon = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Advantage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Flanked = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConditionId? TargetHasCondition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HpBelow = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HeightAbove = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsSpell = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DamageType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetTag = null);

/// <summary>Whom a passive's effects land on. JSON: <c>"other"</c> (the default) or <c>"self"</c>.</summary>
public enum PassiveTarget
{
    /// <summary>The hook's other unit (<see cref="HookArgs.Other"/>): the attack's target, whoever dealt the damage, ….
    /// The passive doesn't fire when there is none, or when it is the owner.</summary>
    [JsonStringEnumMemberName("other")] Other,
    /// <summary>The owner.</summary>
    [JsonStringEnumMemberName("self")] Self,
}

/// <summary>
/// A passive feature (spec §4.4): <see cref="Static"/> modifiers that always hold, and <see cref="Effects"/> that run on
/// <see cref="Hook"/> when <see cref="When"/> holds, on <see cref="Target"/>, at most once per turn (anyone's), once per
/// round or <see cref="UsesPerBattle"/> times. The effects come from the owner, or, for a condition's passive, from the
/// unit that applied the condition. <see cref="Replaces"/> lets a later pack override an id.
/// </summary>
public sealed record PassiveDef(string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HookKind? Hook,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WhenDef? When,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<EffectDef> Effects,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StaticMods? Static,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OncePerTurn = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OncePerRound = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsesPerBattle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Replaces = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PassiveTarget Target = PassiveTarget.Other);

/// <summary>
/// A condition (spec §3.7): its <see cref="Static"/> modifiers and <see cref="Passives"/> hold while a unit has it.
/// <see cref="Tags"/> (<c>"magical"</c>, <c>"physical"</c>, <c>"fear"</c>, …) are what <c>cleanse</c> matches;
/// <see cref="Implies"/> names a condition that comes with this one (stunned implies incapacitated).
/// </summary>
public sealed record ConditionDef(ConditionId Id, string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StaticMods? Static,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<PassiveDef> Passives,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Tags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConditionId? Implies = null);

/// <summary>An encounter trigger. Kept raw until Task 22 gives it typed When/Do records; Task 4–21 battles carry none.</summary>
public sealed record TriggerDef(string Id, JsonElement When, bool Once, JsonElement Do);

/// <summary>
/// A lasting area effect on the map (e.g. a wall of fire or a difficult-terrain spell), numbered by
/// <see cref="BattleState.NextSerial"/>. Task 10 gives zones their behaviour.
/// </summary>
public sealed record Zone(int Serial, UnitId? Owner, string Kind, ImmutableArray<Tile> Tiles, int? RoundsLeft,
    bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives);
