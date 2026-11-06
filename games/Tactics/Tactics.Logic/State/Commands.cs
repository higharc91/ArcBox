using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>What a phone or the AI asks a unit to do. <see cref="Battle.Apply"/> validates and resolves it.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Move), nameof(Move))]
[JsonDerivedType(typeof(Use), nameof(Use))]
[JsonDerivedType(typeof(Delay), nameof(Delay))]
[JsonDerivedType(typeof(React), nameof(React))]
[JsonDerivedType(typeof(EndTurn), nameof(EndTurn))]
public abstract record Command([property: JsonPropertyOrder(-1)] UnitId Actor);

/// <summary>Walk along <see cref="Path"/>, which starts at the actor's tile.</summary>
public sealed record Move(UnitId Actor, ImmutableArray<Tile> Path) : Command(Actor);

/// <summary>
/// Use an ability (attacks, spells, Dash, Shove, Help, …) on a target. To use a consumable, the ability is
/// <c>common.use_item</c> and <see cref="Item"/> names the item from the actor's bag; the item's own ability runs.
/// </summary>
public sealed record Use(UnitId Actor, AbilityId Ability, Target Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ItemId? Item = null) : Command(Actor)
{
    /// <summary>
    /// For a weapon attack aimed at one unit (<see cref="AbilityDef.IsWeaponAttack"/>), the target of each of its attacks in
    /// order, one per attack (<see cref="UnitSheet.Attacks"/>), the first being <see cref="Target"/>'s unit. A shorter list
    /// repeats its last entry. Empty (the default, and what a command from before it reads as) means every attack at
    /// <see cref="Target"/>. Whatever the list says, an attack whose target has fallen goes on to another (see
    /// <see cref="StrikeStep"/>). Left out of the JSON when empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<UnitId> Targets { get; init; }

    /// <summary>Equal when every field is, the <see cref="Targets"/> compared entry by entry (an empty list equals none).</summary>
    public bool Equals(Use? other)
        => other is not null && base.Equals(other) && Ability == other.Ability && Target == other.Target && Item == other.Item
           && (Targets.IsDefaultOrEmpty ? other.Targets.IsDefaultOrEmpty : !other.Targets.IsDefault && Targets.SequenceEqual(other.Targets));

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Ability, Target, Item, Targets.IsDefaultOrEmpty ? 0 : Targets.Length);
}

/// <summary>Move the actor's turn <see cref="Slots"/> places later in the current round.</summary>
public sealed record Delay(UnitId Actor, int Slots) : Command(Actor);

/// <summary>Answer the pending reaction offer.</summary>
public sealed record React(UnitId Actor, ReactionChoice Choice) : Command(Actor);

/// <summary>End the actor's turn.</summary>
public sealed record EndTurn(UnitId Actor) : Command(Actor);

/// <summary>What an ability is aimed at: a unit, a tile, or neither (self).</summary>
public sealed record Target(UnitId? Unit = null, Tile? Tile = null);
