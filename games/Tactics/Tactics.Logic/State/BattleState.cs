using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>The objectives in force and their progress counters (e.g. rounds survived).</summary>
public sealed record ObjectiveState(ObjectivesDef Current,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Progress);

/// <summary>
/// A battle at rest between commands: immutable, and serialisable with <c>ArcJson.Options</c> so it can be saved and
/// resumed exactly, mid-reaction included.
/// </summary>
/// <param name="ContentHash">The hash of the rules pack the battle runs under.</param>
/// <param name="MapId">The base map; the rules pack resolves it (<see cref="RulesPack.MapFor"/>).</param>
/// <param name="Units">Every unit, including Downed, Out and Dead ones.</param>
/// <param name="Round">The current round, from 1.</param>
/// <param name="Order">This round's turn order.</param>
/// <param name="TurnIndex">The index in <paramref name="Order"/> of the unit whose turn it is.</param>
/// <param name="Rng">The battle's random generator; every die comes from it.</param>
/// <param name="Queue">Steps still to resolve, head at index 0.</param>
/// <param name="Pending">A decision the battle waits for, if any.</param>
/// <param name="HeightDelta">Height changes to the base map, by tile.</param>
/// <param name="Zones">Lasting area effects on the map.</param>
/// <param name="Objectives">Objectives in force and their progress.</param>
/// <param name="Flags">Story flags set during the battle.</param>
/// <param name="Counters">Named counters for objectives and triggers.</param>
/// <param name="FiredTriggers">Ids of once-only triggers that have fired.</param>
/// <param name="Outcome">Set once the battle has ended.</param>
/// <param name="Injuries">Injuries gained by units that went Out.</param>
/// <param name="ItemsUsed">Consumables used, in order.</param>
/// <param name="NextSerial">The next serial for zones and spawned units.</param>
public sealed record BattleState(
    string ContentHash, string MapId, ImmutableSortedDictionary<UnitId, UnitState> Units, int Round,
    ImmutableArray<UnitId> Order, int TurnIndex, Rng Rng, ImmutableList<Step> Queue, Pending? Pending,
    ImmutableSortedDictionary<Tile, int> HeightDelta, ImmutableArray<Zone> Zones, ObjectiveState Objectives,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Flags,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Counters,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> FiredTriggers,
    BattleOutcome? Outcome, ImmutableArray<RecordedInjury> Injuries, ImmutableArray<ItemUse> ItemsUsed, int NextSerial)
{
    /// <summary>The unit whose turn it is: <c>Order[TurnIndex]</c> while the battle has no outcome, otherwise null.</summary>
    [JsonIgnore]
    public UnitId? Active => Outcome is null && TurnIndex >= 0 && TurnIndex < Order.Length ? Order[TurnIndex] : null;
}
