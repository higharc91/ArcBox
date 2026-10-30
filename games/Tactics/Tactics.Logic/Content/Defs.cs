using System.Collections.Immutable;
using System.Text.Json;

namespace Tactics.Logic;

/// <summary>An ability from the content. Placeholder: Task 5 adds cost, targeting and effects.</summary>
public sealed record AbilityDef(AbilityId Id, string Name);

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
