using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>A Downed unit's countdown already lost to one hit (<see cref="Resolution"/>) while that hit resolves.</summary>
public readonly record struct HitMark(int Resolution, UnitId Unit) : IComparable<HitMark>
{
    /// <summary>Orders by resolution, then unit.</summary>
    public int CompareTo(HitMark other)
    {
        var byResolution = Resolution.CompareTo(other.Resolution);
        return byResolution != 0 ? byResolution : Unit.CompareTo(other.Unit);
    }
}

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
/// <param name="Order">This round's turn order: every unit still in the battle (Up or Downed), except those
/// <see cref="Joining"/> it next round. A unit that leaves (Out or Dead) is taken out of it, at the end of its turn if it is
/// the one acting.</param>
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
/// <param name="Outcome">Set once the battle has ended (see <see cref="Objectives"/>).</param>
/// <param name="Injuries">Injuries gained by units that went Out.</param>
/// <param name="ItemsUsed">Consumables used, in order.</param>
/// <param name="NextSerial">The next serial for zones and spawned units.</param>
/// <param name="StartedThisRound">Units whose turn has started (or been skipped) this round; a later start for one of them
/// is a resumed (delayed) turn, which skips turn-start effects and keeps the Reaction.</param>
/// <param name="DelayedThisRound">Units that delayed this round (a unit may delay once per round).</param>
/// <param name="Xp">The encounter's XP award, from the setup.</param>
/// <param name="Loot">Loot tables to roll on a win, from the setup.</param>
/// <param name="Difficulty">The encounter's difficulty, from the setup.</param>
/// <param name="Triggers">The encounter's triggers, from the setup.</param>
public sealed record BattleState(
    string ContentHash, string MapId, ImmutableSortedDictionary<UnitId, UnitState> Units, int Round,
    ImmutableArray<UnitId> Order, int TurnIndex, Rng Rng, ImmutableList<Step> Queue, Pending? Pending,
    ImmutableSortedDictionary<Tile, int> HeightDelta, ImmutableArray<Zone> Zones, ObjectiveState Objectives,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> Flags,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedDictionary<string, int> Counters,
    [property: JsonConverter(typeof(OrdinalJsonConverter))] ImmutableSortedSet<string> FiredTriggers,
    BattleOutcome? Outcome, ImmutableArray<RecordedInjury> Injuries, ImmutableArray<ItemUse> ItemsUsed, int NextSerial,
    ImmutableSortedSet<UnitId> StartedThisRound, ImmutableSortedSet<UnitId> DelayedThisRound, int Xp,
    ImmutableArray<LootTableId> Loot, Difficulty Difficulty, ImmutableArray<TriggerDef> Triggers)
{
    /// <summary>
    /// The number the next resolution gets: each attack, and each run of an effect list on a target, is one resolution
    /// (one "hit"), numbered while a command resolves (see <see cref="RunEffectsStep.Resolution"/>). Reset to 0, with
    /// <see cref="DownedHits"/>, once the queue is empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int NextResolution { get; init; }

    /// <summary>The Downed units whose countdown a resolution still under way has already hit (one tick per hit).</summary>
    public ImmutableSortedSet<HitMark> DownedHits { get; init; } = [];

    /// <summary>
    /// Units that arrived this round (summons): in the battle but not yet in <see cref="Order"/>, which they join when the
    /// next round's order is sorted (see <see cref="TurnOrder.Sort"/>).
    /// </summary>
    public ImmutableSortedSet<UnitId> Joining { get; init; } = [];

    /// <summary>The unit whose turn it is: <c>Order[TurnIndex]</c> while the battle has no outcome, otherwise null.</summary>
    [JsonIgnore]
    public UnitId? Active => Outcome is null && TurnIndex >= 0 && TurnIndex < Order.Length ? Order[TurnIndex] : null;
}
