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

/// <summary>A passive feature from the content. Placeholder: Task 7 adds its hook, condition and effects.</summary>
public sealed record PassiveDef(string Id);

/// <summary>A condition from the content. Placeholder: Task 7 adds its modifiers and passives.</summary>
public sealed record ConditionDef(ConditionId Id, string Name);

/// <summary>An encounter trigger. Kept raw until Task 22 gives it typed When/Do records; Task 4–21 battles carry none.</summary>
public sealed record TriggerDef(string Id, JsonElement When, bool Once, JsonElement Do);

/// <summary>
/// A lasting area effect on the map (e.g. a wall of fire or a difficult-terrain spell), numbered by
/// <see cref="BattleState.NextSerial"/>. Task 10 gives zones their behaviour.
/// </summary>
public sealed record Zone(int Serial, UnitId? Owner, string Kind, ImmutableArray<Tile> Tiles, int? RoundsLeft,
    bool Concentration, bool Difficult, ImmutableArray<PassiveDef> Passives);
